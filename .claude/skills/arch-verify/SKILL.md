---
name: arch-verify
description: Verify a change to the Arch repo — build, the 407-test suite, and the tools/golden.sh byte-identical-output net, including the stash/accept/pop protocol that keeps the golden baseline honest and the Arch.Cli end-to-end run that golden.sh structurally cannot see. Use before committing anything in this repo, and whenever generated site output might have changed (site.css, site.js, PageShell, any Site/Pages/*.cs, model.json fields).
allowed-tools: Bash, PowerShell, Read, Edit, Grep, Glob
---

# Verifying a change to Arch

Arch's whole value is that it generates the same site from the same input. The
regression net is three layers, and **each catches things the others structurally
cannot**. Run all three.

```bash
dotnet build Arch.slnx --nologo        # expect 0 warnings, 0 errors
dotnet test  Arch.slnx --nologo        # expect 407 passed, 0 failed  (~90s, not a hang)
bash tools/golden.sh                   # expect GOLDEN OK
```

`dotnet test` takes ~90 seconds — 209 of those tests are the code analyzer's. That is
normal, not a hang. A "file is locked" build failure is environmental (a running exe or
IDE holds the output); close it and rebuild.

## golden/ does not survive a clone

It is deliberately gitignored — 81 generated HTML files would make every future diff
unreadable. After a fresh clone there is no baseline at all:

```bash
bash tools/golden.sh accept   # expect "golden accepted: 81 files"
```

Accept it **on a clean commit, before changing anything**, or the baseline is worthless.

## Never accept a golden on top of the code you are verifying

Accepting over your own change launders it into the baseline and proves nothing. If the
tree is already dirty when you realise you need a baseline:

```bash
git stash push -m wip <the-files-you-changed>
bash tools/golden.sh accept      # baseline is now definitely clean HEAD
git stash pop
bash tools/golden.sh             # the diff is now definitely yours
```

## When the golden legitimately changes

Some changes are *supposed* to alter output (a new model.json field, a stylesheet fix).
That is fine — accepting a **sanctioned, understood, predicted** delta is not laundering.
The discipline is confirming the delta is exactly what you predicted, first:

```bash
# 1. Which files changed? Should be only the ones you expect.
diff -rq --strip-trailing-cr golden/code work/golden/code
diff -rq --strip-trailing-cr golden/sql  work/golden/sql

# 2. What was REMOVED? Additive changes should show zero.
diff --strip-trailing-cr golden/code/assets/site.css work/golden/code/assets/site.css | grep "^<"
```

Read every removed line and account for it. A modified-in-place rule shows as a `<`/`>`
pair — that is expected; an unpaired `<` is a deletion you did not intend. Only then
`bash tools/golden.sh accept`, and say in the commit message which delta you accepted
and why.

## Verify the check actually fired

A green result from a check that matched nothing is worse than no check. The golden
harness once passed for two runs while its path normalisation matched nothing at all —
unnormalised absolute paths are constant on one machine, so the comparison was perfectly
happy, and the failure would have surfaced months later on someone else's clone.

```bash
grep -rl "Documents.Code.Arch" golden    # MUST print nothing
```

## The layer golden.sh cannot see

`tools/golden.sh` runs `Arch.Code` and `Arch.Sql` **directly**. It never runs `Arch.Cli`.
Those are different binaries with different settings — notably `Arch.Cli` must run
`InvariantGlobalization=false` because it hosts `Arch.Sql`, while the other two force
invariant. A real culture-dependent formatting bug (`:P0` rendering `"75 %"` instead of
`"75%"`) sat exposed through all of Phase 5 with `golden.sh` green and `dotnet test`
green, because nothing compared cross-process output.

So if you touched anything that formats numbers, dates, or percentages — or added a
project — also run:

```bash
dotnet run --project src/Arch.Cli -- tests/Arch.Code.Tests/Fixtures/SampleRepo \
  --out work/check --no-open
diff -r --strip-trailing-cr work/check golden/code
```

Expect differences **only** in the timestamp and absolute-path lines (golden's
`normalise()` does not run on `work/check`). Anything else is a real divergence.

## The fixture path trap

`tools/golden.sh` scans `tests/Arch.Code.Tests/Fixtures/SampleRepo` — **not** the parent
`tests/Arch.Code.Tests/Fixtures`, which also holds fixtures for other tests. Running a
manual comparison against the parent produces a huge, scary diff that is 100% an artifact
of scanning the wrong folder. Read `tools/golden.sh` for the exact path; never
reconstruct it from memory.

## Editing files in bulk

There is no python on this machine, so bulk edits go through `sed` — and `sed -i` eats
CRLF. Checked-in files are CRLF here (generated output is LF). After any `sed -i` on a
tracked file:

```bash
sed -i 's/$/\r/' <file>
```
