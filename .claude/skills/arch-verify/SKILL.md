---
name: arch-verify
description: Verify a change to the Arch repo — build, the 623-test suite, and the tools/golden.sh byte-identical-output net (which now also runs a combined-mode Arch.Cli fixture and arch landscape, not just Arch.Code/Arch.Sql directly), including the stash/accept/pop protocol that keeps the golden baseline honest. Use before committing anything in this repo, and whenever generated site output might have changed (site.css, site.js, PageShell, any Site/Pages/*.cs, model.json fields).
allowed-tools: Bash, PowerShell, Read, Edit, Grep, Glob
---

# Verifying a change to Arch

Arch's whole value is that it generates the same site from the same input. The
regression net is three layers, and **each catches things the others structurally
cannot**. Run all three.

```bash
dotnet build Arch.slnx --nologo        # expect 0 warnings, 0 errors
dotnet test  Arch.slnx --nologo        # expect 623 passed, 0 failed  (~90s, not a hang)
bash tools/golden.sh                   # expect GOLDEN OK
```

`dotnet test` takes ~90 seconds — most of those tests are the code analyzer's. That is
normal, not a hang. A "file is locked" build failure is environmental (a running exe or
IDE holds the output); close it and rebuild.

## golden/ does not survive a clone

It is deliberately gitignored — 173 generated HTML/JSON files (code + sql + a combined-
mode Arch.Cli run + a landscape run) would make every future diff unreadable. After a
fresh clone there is no baseline at all:

```bash
bash tools/golden.sh accept   # expect "golden accepted: 173 files"
```

`tools/golden.sh` also has a CI-portable `manifest` / `manifest-check` mode — a committed
`tools/golden.manifest` (one `<sha256> <path>` line per file, LF-normalised before
hashing) instead of the whole `golden/` tree — used by the two-OS CI matrix
(`.github/workflows/ci.yml`), since `golden/` itself only ever exists locally.

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

## Arch.Cli is no longer a blind spot — but read this before assuming it's fully covered

`tools/golden.sh` used to run only `Arch.Code` and `Arch.Sql` **directly**, never
`Arch.Cli`. It now also runs a third fixture (`CrossLink/ShopTest`, both a `.csproj` and a
`.sql` file) through `src/Arch.Cli/Arch.Cli.csproj` in combined mode — exercising
`Runner.cs`, `HubPage`, `CrossLink`, `DedupeVendorAssets` — plus a fourth run of
`arch landscape` over the results. So a real change to any of those IS now caught by
`bash tools/golden.sh` like everything else.

What this still does not give you: `Arch.Cli` runs with `InvariantGlobalization=false`
(it hosts `Arch.Sql`), while `Arch.Code`/`Arch.Sql` standalone force invariant — and this
machine's own locale is still whatever it is on every run, so a culture-dependent
formatting bug (`:P0` rendering `"75 %"` instead of `"75%"`) only shows up here if this
machine's culture would actually produce the difference. A real fix for that needs a
non-English-locale run, which `golden.sh` does not attempt. If you touched anything that
formats numbers, dates, or percentages, that risk is still yours to reason about
separately — the fixture coverage gap is closed, the locale-coverage gap is not.

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
