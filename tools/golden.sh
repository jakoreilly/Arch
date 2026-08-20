#!/usr/bin/env bash
# Regenerates code/sql/cli/landscape output from the in-repo fixtures and compares it to a
# golden baseline. This is the regression net for the whole migration and for the ongoing
# CI/CD rollout (plan.md): determinism must hold byte-for-byte, and this is what proves it.
#
#   tools/golden.sh accept          -- regenerate and store as the new golden/ tree (local)
#   tools/golden.sh                 -- regenerate and diff against golden/ (local)
#   tools/golden.sh manifest        -- regenerate and write tools/golden.manifest (CI-portable)
#   tools/golden.sh manifest-check  -- regenerate and diff against tools/golden.manifest
#
# golden/ and work/ are gitignored: golden/ is a local net (thousands of files, including a
# 3.3MB vendored bundle) and is not meant to be committed. tools/golden.manifest — one
# "<sha256>  <path>" line per file — IS meant to be committed and is what the two-OS CI job
# checks against, since it is small and portable in a way the tree itself is not.
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

# The values that legitimately change between runs and would otherwise make every
# comparison fail: the generation timestamp stamped into pages, the absolute source path
# written verbatim into model.json, and the repository commit count. Normalised here
# rather than by adding a --deterministic flag to the tools, which would be production
# surface added for a test.
#
# The commit count is the subtle one. The code fixture lives INSIDE this repository, so
# GitHistory.Analyze reports *Arch's own* commit total into the Evolution page and
# model.json - a number that increments with every commit made while working the plan.
# Left alone it makes the golden fail on the next commit no matter what changed, which
# trains you to run `accept` reflexively and destroys the net's whole value.
#
# The source-link "ref" is the same class of problem, for the same reason: the fixture is
# inside this repo, so GitRemote.Detect bakes the CURRENT BRANCH NAME into model.json and
# into every page's window.ARCH_SOURCELINK. Without this the golden would fail on every
# branch switch. The remote's BASE url is not normalised on purpose - it is stable for this
# repo, and leaving it visible is what proves the derivation still works (and that no
# credential ever appears in it).
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
  # The sourcePath clause normalises the VALUE rather than matching the root path: model.json
  # JSON-escapes every backslash, so the root appears there with every separator DOUBLED, which none of
  # the three $esc_* patterns above match. Left alone, the developer's home directory survives
  # normalisation and gets hashed into tools/golden.manifest — which then only ever matches on
  # the one machine that generated it. Same shape as the "ref" and "toolVersion" clauses.
  find "$dir" -type f \( -name '*.html' -o -name '*.xhtml' -o -name '*.json' \
                      -o -name '*.md' -o -name '*.js' \) -print0 |
    while IFS= read -r -d '' f; do
      sed -i \
        -e "s#$esc_win#<ROOT>#g" \
        -e "s#$esc_win_fwd#<ROOT>#g" \
        -e "s#$esc_root#<ROOT>#g" \
        -e 's#"sourcePath": "[^"]*"#"sourcePath": "<ROOT>"#g' \
        -e 's#[0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}[ T][0-9]\{2\}:[0-9]\{2\}\(:[0-9]\{2\}\)\?#<TIMESTAMP>#g' \
        -e 's#[0-9]\{4\}-[0-9]\{2\}-[0-9]\{2\}#<DATE>#g' \
        -e 's#"totalCommits": [0-9]\{1,\}#"totalCommits": <COMMITS>#g' \
        -e 's#"ref": "[^"]*"#"ref": "<REF>"#g' \
        -e 's#"ref":"[^"]*"#"ref":"<REF>"#g' \
        -e 's#"toolVersion": "[^"]*"#"toolVersion": "<TOOL>"#g' \
        -e 's#"toolVersion":"[^"]*"#"toolVersion":"<TOOL>"#g' \
        -e 's#<div class="num">[0-9]\{1,\}</div><div class="lbl">Commits in history</div>#<div class="num">\&lt;COMMITS\&gt;</div><div class="lbl">Commits in history</div>#g' \
        "$f"
    done
}

# Generates only. normalise() is NOT called here on purpose: it rewrites "totalCommits": 123
# to "totalCommits": <COMMITS>, which is no longer valid JSON, and the landscape run below
# READS these model.json files back. Normalising before it ran left SiteDiscovery unable to
# parse a single site — it caught the JsonException, recorded a diagnostic nothing surfaced,
# reported "found 0 site(s)", and baselined an empty estate that proved nothing. Every tree is
# normalised together after the landscape instead.
run() { # run <name> <exe-project> <fixture> [extra args...]
  local name="$1" proj="$2" fixture="$3"; shift 3
  dotnet run --project "$proj" --no-build -- "$fixture" --out "$WORK/$name" --no-open "$@" >/dev/null
}

run code src/Arch.Code/Arch.Code.csproj tests/Arch.Code.Tests/Fixtures/SampleRepo
run sql  src/Arch.Sql/Arch.Sql.csproj   tests/Arch.Sql.Tests/Fixtures --dialect tsql

# Arch.Cli (plan.md Phase 4): the two runs above exercise Arch.Code and Arch.Sql directly and
# structurally cannot see Runner.cs, HubPage, CrossLink, DedupeVendorAssets, or the landscape —
# exactly the surface a company-wide pipeline actually runs. CrossLink/ShopTest has both a
# .csproj and a .sql file, so this is combined mode: a hub page plus code/ and sql/ subsites.
run cli src/Arch.Cli/Arch.Cli.csproj tests/Arch.Cli.Tests/Fixtures/CrossLink/ShopTest

# Landscape reads no source — only the model.json files the runs above already wrote — so it
# federates $WORK itself: "code" is a single-provider site (model.json at its root), "cli" is
# combined mode (model.json under cli/code/), and "sql" is skipped as SQL-only, all exactly the
# shapes SiteDiscovery.Discover probes for. Must run after every `run` call above, and its own
# --out must stay a location `run` never populated (SiteDiscovery enumerates $WORK's
# subdirectories AT THIS POINT, so a pre-existing "landscape" folder would be scanned as a
# fourth "site" and fail to parse as either model shape).
dotnet run --project src/Arch.Cli/Arch.Cli.csproj --no-build -- landscape "$WORK" \
  --out "$WORK/landscape" --no-open >/dev/null

# Now that nothing reads them back, flatten the values that legitimately vary per run. Order
# matters: see the comment on run() above.
normalise "$WORK/code"
normalise "$WORK/sql"
normalise "$WORK/cli"
normalise "$WORK/landscape"

# manifest mode: a committed <sha256>  <relative-path> list instead of a committed golden/
# tree — golden/ itself is gitignored (thousands of files, including a 3.3MB vendored bundle)
# and only ever exists locally, which is exactly why the two-OS CI job in plan.md's Phase 4
# needs something smaller and portable to check against. LF-normalised before hashing: CRLF
# vs LF is already handled for the `diff` path by --strip-trailing-cr below, but a hash of two
# byte-different-but-content-identical files never matches without this.
if [ "$MODE" = "manifest" ]; then
  : > tools/golden.manifest
  find "$WORK/code" "$WORK/sql" "$WORK/cli" "$WORK/landscape" -type f -print0 |
    while IFS= read -r -d '' f; do
      rel="${f#"$WORK"/}"
      printf '%s  %s\n' "$(tr -d '\r' < "$f" | sha256sum | cut -d' ' -f1)" "$rel"
    done | LC_ALL=C sort -k2 > tools/golden.manifest
  echo "manifest written: $(wc -l < tools/golden.manifest) files"
  exit 0
fi

if [ "$MODE" = "manifest-check" ]; then
  if [ ! -f tools/golden.manifest ]; then
    echo "no golden manifest yet — run: tools/golden.sh manifest"
    exit 1
  fi
  ACTUAL="$WORK/golden.manifest.actual"
  find "$WORK/code" "$WORK/sql" "$WORK/cli" "$WORK/landscape" -type f -print0 |
    while IFS= read -r -d '' f; do
      rel="${f#"$WORK"/}"
      printf '%s  %s\n' "$(tr -d '\r' < "$f" | sha256sum | cut -d' ' -f1)" "$rel"
    done | LC_ALL=C sort -k2 > "$ACTUAL"
  if diff tools/golden.manifest "$ACTUAL"; then
    echo "GOLDEN OK (manifest)"
    exit 0
  else
    echo "GOLDEN CHANGED — review the diff above. If intended, run: tools/golden.sh manifest"
    exit 1
  fi
fi

if [ "$MODE" = "accept" ]; then
  rm -rf golden; mkdir -p golden
  cp -r "$WORK/code" "$WORK/sql" "$WORK/cli" "$WORK/landscape" golden/
  echo "golden accepted: $(find golden -type f | wc -l) files"
  exit 0
fi

if [ ! -d golden ]; then
  echo "no golden tree yet — run: tools/golden.sh accept"
  exit 1
fi

# --strip-trailing-cr: .gitattributes normalises checked-out files to CRLF on Windows
# while generated output is LF, and without this every line reads as changed.
if diff -r --strip-trailing-cr golden/code      "$WORK/code" &&
   diff -r --strip-trailing-cr golden/sql       "$WORK/sql" &&
   diff -r --strip-trailing-cr golden/cli       "$WORK/cli" &&
   diff -r --strip-trailing-cr golden/landscape "$WORK/landscape"; then
  echo "GOLDEN OK"
else
  echo "GOLDEN CHANGED — review the diff above. If intended, run: tools/golden.sh accept"
  exit 1
fi
