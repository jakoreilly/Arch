#!/usr/bin/env sh
# The portable core of Arch's CI integration.
#
# Every forge-specific template beside this file sets a handful of environment variables and
# then runs this script. Nothing forge-specific lives below this line — no CI_*, no GITHUB_*,
# no vendor-only tooling — so supporting a third CI system means writing about ten lines of
# its own YAML, not reimplementing the contract. Keep it that way: a `if [ "$GITHUB_ACTIONS" ]`
# here would be the first crack.
#
# POSIX sh on purpose (not bash): the published Arch container is a plain .NET runtime image
# plus git, and some hosted runners' default shell is dash.
#
#   ARCH_PATH                folder to analyse                        (default ".")
#   ARCH_OUT                 output folder                            (default "artifacts/arch")
#   ARCH_FAIL_ON             --fail-on gate list; "" disables gating  (default "secrets,cycles")
#   ARCH_BLOCK_MERGE         "true" = a non-zero arch result fails the job. Anything else
#                            reports and exits 0, so adopting this is never by itself a new
#                            merge gate.                              (default "false")
#   ARCH_SOURCE_LINK_TYPE    github | gitlab | vscode | local — omit to let Arch auto-detect
#   ARCH_SOURCE_LINK_BASE    repo web URL, when _TYPE is set
#   ARCH_SOURCE_LINK_REF     commit sha or branch, when _TYPE is set
#   ARCH_SARIF               path for the SARIF log; "" to skip       (default "")
#   ARCH_EXTRA_ARGS          extra flags, word-split on purpose       (default "")
#   ARCH_BIN                 the executable                           (default "arch")
#
# Exit codes are Arch's own (HOW-TO-USE.md, "Exit codes"): 0 clean, 1 crash, 2 usage error,
# 3 a gate tripped and the site explaining it was still written. This script never invents a
# code of its own; it only decides whether a non-zero one fails the job.
set -eu

ARCH_PATH="${ARCH_PATH:-.}"
ARCH_OUT="${ARCH_OUT:-artifacts/arch}"
ARCH_FAIL_ON="${ARCH_FAIL_ON:-secrets,cycles}"
ARCH_BLOCK_MERGE="${ARCH_BLOCK_MERGE:-false}"
ARCH_SARIF="${ARCH_SARIF:-}"
ARCH_EXTRA_ARGS="${ARCH_EXTRA_ARGS:-}"
ARCH_BIN="${ARCH_BIN:-arch}"

# --no-snippets: belt-and-braces beside SecretScrub. The scrubber blanks secret-shaped
# literals, but a CI-published site aimed at a whole company has no need of inline source at
# all — the source-link button goes to the real file, with the reader's own permissions.
set -- "$ARCH_PATH" --out "$ARCH_OUT" --no-open --no-snippets --redact-source-path

if [ -n "$ARCH_FAIL_ON" ]; then
    set -- "$@" --fail-on "$ARCH_FAIL_ON"
fi
if [ -n "$ARCH_SARIF" ]; then
    set -- "$@" --sarif "$ARCH_SARIF"
fi
# All three or none: --source-link-base without --source-link-type is a usage error, and a
# half-set trio is the most common way a template breaks after a forge migration.
if [ -n "${ARCH_SOURCE_LINK_TYPE:-}" ]; then
    set -- "$@" --source-link-type "$ARCH_SOURCE_LINK_TYPE" \
                --source-link-base "${ARCH_SOURCE_LINK_BASE:?ARCH_SOURCE_LINK_BASE is required when ARCH_SOURCE_LINK_TYPE is set}" \
                --source-link-ref "${ARCH_SOURCE_LINK_REF:?ARCH_SOURCE_LINK_REF is required when ARCH_SOURCE_LINK_TYPE is set}"
fi

echo "arch: $ARCH_BIN $* $ARCH_EXTRA_ARGS"
ARCH_RC=0
# ARCH_EXTRA_ARGS is deliberately unquoted: it must word-split, that is the whole point of it.
# shellcheck disable=SC2086
"$ARCH_BIN" "$@" $ARCH_EXTRA_ARGS || ARCH_RC=$?

# Publish-on-change: model.json's top-level contentHash is a SHA-256 of the ANALYSIS. It
# excludes the absolute source path, git churn and diagnostics, so it only moves when
# something a reader would care about moved. Written beside the site so the publish step can
# compare it against the last published one and skip an unchanged upload.
#
# grep/sed rather than jq or python: the Arch container carries git and nothing else, and
# this is the exact form HOW-TO-USE.md documents. tail -1 because files[] each carry their
# own contentHash and the top-level one is written last.
ARCH_MODEL="$ARCH_OUT/code/model.json"          # combined mode (code + sql)
[ -f "$ARCH_MODEL" ] || ARCH_MODEL="$ARCH_OUT/model.json"   # single-provider mode
if [ -f "$ARCH_MODEL" ]; then
    grep -o '"contentHash": "[a-f0-9]*"' "$ARCH_MODEL" \
        | tail -1 | sed 's/.*"\([a-f0-9]*\)"$/\1/' > "$ARCH_OUT.hash"
    echo "arch: content hash $(cat "$ARCH_OUT.hash")"
fi

case "$ARCH_RC" in
    0) echo "arch: clean"; exit 0 ;;
    3) ARCH_MSG="arch: quality gate tripped — the site explaining it is in $ARCH_OUT" ;;
    2) ARCH_MSG="arch: usage error — this template or the repo's overrides are wrong" ;;
    *) ARCH_MSG="arch: FAILED with exit $ARCH_RC — this is an Arch bug, please report it" ;;
esac
echo "$ARCH_MSG"

if [ "$ARCH_BLOCK_MERGE" = "true" ]; then
    exit 1
fi
echo "arch: block_merge is not 'true' — reporting only, not failing the job."
exit 0
