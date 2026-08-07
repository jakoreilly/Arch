# Arch

Two static-site generators that read a folder and explain what is in it, sharing one
core and one `arch` executable.

- **`Arch.Code`** (`archdiagram`) — analyses source code: dependency graphs, modules,
  metrics, hotspots, git evolution.
- **`Arch.Sql`** (`archsql`) — analyses SQL scripts, or connects to a live server.
- **`Arch.Cli`** (`arch`) — detects which applies to a folder and runs it; runs both and
  writes a hub page when both apply; cross-links code→SQL when a connection string in
  the code matches the SQL model's catalog. `arch landscape <parent>` federates the sites
  already generated under a folder into one estate view (shared databases, cross-repo
  package edges, service calls) — it reads their `model.json` files, never source.

Output is a **fully offline** static site — no CDN, no network, opens from `file://`.

## Layout

```
src/Arch.Core/    Html, Crumbs, PageShell, Glossary, SiteAssets, ModelJson,
                    IAnalysisProvider/Detection, and the shared assets (site.css/js)
src/Arch.Code/    namespace Arch.Code.*, assets-code overlay
src/Arch.Sql/     namespace Arch.Sql.*,  assets-sql overlay
src/Arch.Cli/     the unified exe: Entry (the verb table), Runner, HubPage, CrossLink
tests/            Arch.Code.Tests (256), Arch.Sql.Tests (183), Arch.Cli.Tests (22)
tools/golden.sh   the byte-identical-output regression net
```

## Read these before starting work

- **[continue.md](continue.md)** — where work stopped, and the findings from each phase.
  Start here. It has a "Do not re-litigate" section; respect it.
- **[plan.md](plan.md)** — the source of truth for the migration: hard constraints, and a
  GOTCHA block for every trap found so far.

## Commands

```bash
dotnet build Arch.slnx --nologo     # 0 warnings, 0 errors
dotnet test  Arch.slnx --nologo     # 461 passed (~90s — not a hang)
bash tools/golden.sh                # GOLDEN OK
bash tools/golden.sh accept         # re-baseline (golden/ is gitignored; regenerate after a clone)
```

**Use the `arch-verify` skill before committing.** Golden has a protocol — never accept a
baseline on top of the change you are verifying — and it cannot see `Arch.Cli` at all.

**Use the `arch-site-design` skill before touching generated markup or styling.**

## Conventions

- **Determinism is the product.** Same input, same bytes out. Anything that varies per
  run (timestamps, absolute paths, commit counts) is normalised by `tools/golden.sh`,
  not by a flag on the tool.
- **Never emit a raw hex colour** from C#. Style through the CSS tokens
  (`var(--accent)`, `var(--warn)`) or the dark theme breaks silently.
- **`model.json` fields are appended last** and are additive — a new field shows up in
  the golden diff as exactly one new line and nothing else.
- **Read-only.** Both analysers only ever read the folder they are pointed at.
  `arch connect` opens a read-only connection. Never write to the analysed source.
- **Secrets never reach the output.** Connection strings are scanned and reported by
  *shape*; passwords must not appear in any generated file.
- **`.gitignore` ignores `*.md` by default** and re-includes named docs one by one. A new
  Markdown file is silently untracked until you add a `!` line for it — `git status` will
  not mention it at all. Check with `git check-ignore -v <file>`.
- **There are two `SiteGenerator.cs` files, and only one is live.**
  `src/Arch.Code/SiteGenerator.cs` (`Arch.Code.SiteGenerator`) is the one every caller uses.
  `src/Arch.Code/Site/SiteGenerator.cs` (`Arch.Code.Site.SiteGenerator`) is an unreferenced
  stale copy that already lacks the Evolution and Explore pages. Adding a page to the wrong
  one compiles cleanly, passes every test, and silently does nothing. Grep for the caller
  (`SiteGenerator.Generate`) before editing either. **Deleting the dead copy is worth doing.**

## Environment

- .NET SDK 10.0.300, `net10.0`, Windows 11.
- Shell is Git Bash or Windows PowerShell 5.1 — **5.1 has no `&&`, no ternary, no `??`**.
- **No python and no node on this machine.** Bulk edits go through `sed`; `sed -i` eats
  CRLF, so follow it with `sed -i 's/$/\r/' <file>` on tracked files.
- A "file is locked" build failure is environmental — a running exe or IDE holds the
  output. Close it and rebuild.
- A local SQL Server is reachable for testing `arch connect`
  (`Server=localhost;Database=AdventureWorks2022;Trusted_Connection=True;TrustServerCertificate=True;`).
  Keep connection strings in a scratch file, **never** a tracked one.

## Scope

This repo has **no remote** and nothing has been pushed. The original ArchDiagram and
ArchSQL repos are not to be touched — **any change to a file outside `Arch/` is out of
scope; stop and ask.**
