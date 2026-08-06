#!/usr/bin/env bash
# Regenerates both sites from the in-repo fixtures and compares them to the golden
# trees under golden/. This is the regression net for the whole migration: phases 3
# and 4 must leave generated output byte-identical, and this is what proves it.
#
#   tools/golden.sh accept   -- regenerate and store as the new golden
#   tools/golden.sh          -- regenerate and diff against the stored golden
#
# golden/ and work/ are gitignored: the tree is a local net for this migration, not a
# committed artifact. Regenerate it with `accept` on a known-good commit.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
# Native Windows spelling of the same directory (C:/Users/... rather than /c/Users/...).
# `pwd -W` is Git Bash's; on a real POSIX host it fails and the POSIX form is correct.
WIN_ROOT="$(pwd -W 2>/dev/null || pwd)"
MODE="${1:-check}"
WORK="$ROOT/work/golden"
rm -rf "$WORK"; mkdir -p "$WORK"

dotnet build Arch.slnx --nologo -v q >/dev/null

# The two values that legitimately change between runs and would otherwise make every
# comparison fail: the generation timestamp stamped into pages, and the absolute source
# path written verbatim into model.json. Normalised here rather than by adding a
# --deterministic flag to the tools, which would be production surface added for a test.
normalise() {
  local dir="$1"
  # Three spellings of the same root have to be caught, because `pwd` in Git Bash is the
  # POSIX form (/c/Users/...) while the tools emit the native Windows form
  # (C:\Users\...). Normalising only $ROOT silently matches nothing and leaves absolute
  # paths in the golden tree — which still compares equal until the repo is moved, so
  # the failure surfaces months later on someone else's machine.
  # '#' is the sed delimiter because paths contain '/'; the escape handles backslashes
  # and '&', which are sed metacharacters.
  local esc_root esc_win_fwd esc_win
  esc_root="$(printf '%s' "$ROOT" | sed 's#[\\/&]#\\&#g')"                     # /c/Users/...
  esc_win_fwd="$(printf '%s' "$WIN_ROOT" | sed 's#[\\/&]#\\&#g')"              # C:/Users/...
  esc_win="$(printf '%s' "$WIN_ROOT" | tr '/' '\\' | sed 's#[\\/&]#\\&#g')"    # C:\Users\...
  # .xhtml is the wiki export's extension and is NOT matched by '*.html' — it was the
  # one file still carrying an absolute path after the first pass.
  find "$dir" -type f \( -name '*.html' -o -name '*.xhtml' -o -name '*.json' \
                      -o -name '*.md' -o -name '*.js' \) -print0 |
    while IFS= read -r -d '' f; do
      sed -i \
        -e "s#$esc_win#<ROOT>#g" \
        -e "s#$esc_win_fwd#<ROOT>#g" \
        -e "s#$esc_root#<ROOT>#g" \
        -e 's#[0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}[ T][0-9]\{2\}:[0-9]\{2\}\(:[0-9]\{2\}\)\?#<TIMESTAMP>#g' \
        -e 's#[0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}#<DATE>#g' \
        "$f"
    done
}

run() { # run <name> <exe-project> <fixture> [extra args...]
  local name="$1" proj="$2" fixture="$3"; shift 3
  dotnet run --project "$proj" --no-build -- "$fixture" --out "$WORK/$name" --no-open "$@" >/dev/null
  normalise "$WORK/$name"
}

run code src/Arch.Code/Arch.Code.csproj tests/Arch.Code.Tests/Fixtures/SampleRepo
run sql  src/Arch.Sql/Arch.Sql.csproj   tests/Arch.Sql.Tests/Fixtures --dialect tsql

if [ "$MODE" = "accept" ]; then
  rm -rf golden; mkdir -p golden
  cp -r "$WORK/code" "$WORK/sql" golden/
  echo "golden accepted: $(find golden -type f | wc -l) files"
  exit 0
fi

if [ ! -d golden ]; then
  echo "no golden tree yet — run: tools/golden.sh accept"
  exit 1
fi

# --strip-trailing-cr: .gitattributes normalises checked-out files to CRLF on Windows
# while generated output is LF, and without this every line reads as changed.
if diff -r --strip-trailing-cr golden/code "$WORK/code" &&
   diff -r --strip-trailing-cr golden/sql  "$WORK/sql"; then
  echo "GOLDEN OK"
else
  echo "GOLDEN CHANGED — review the diff above. If intended, run: tools/golden.sh accept"
  exit 1
fi
