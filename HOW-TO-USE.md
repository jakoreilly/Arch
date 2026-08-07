# How to use Arch

The complete reference. If you just want a report out of a folder, read
[IDIOTS-GUIDE.md](IDIOTS-GUIDE.md) instead — it's two double-clicks.

- [What Arch is](#what-arch-is)
- [Getting it built](#getting-it-built)
- [The commands](#the-commands)
- [Options](#options)
- [What you get](#what-you-get)
- [The pages](#the-pages)
- [Analysing a live SQL Server](#analysing-a-live-sql-server)
- [Using it in CI](#using-it-in-ci)
- [Guarantees](#guarantees)
- [Known limitations](#known-limitations)
- [Troubleshooting](#troubleshooting)

---

## What Arch is

Two static-site generators sharing one core and one executable:

| | Reads | Produces |
|---|---|---|
| **Arch.Code** (`archdiagram`) | source code | dependency graphs, modules, metrics, hotspots, git evolution |
| **Arch.Sql** (`archsql`) | `.sql` scripts, or a live SQL Server | schema inventory, ER diagram, CRUD matrix, lint, impact analysis |
| **Arch.Cli** (`arch`) | either or both | runs whichever applies; writes a hub page when both do |

Output is a **fully offline** static site: no CDN, no network calls, opens from `file://`.

## Getting it built

`build.cmd`, or directly:

```bash
dotnet build Arch.slnx -c Release --nologo
```

That produces three executables under `src/*/bin/Release/net10.0/`:

| Executable | Use it when |
|---|---|
| `arch.exe` | Almost always. Detects what's in the folder and does the right thing. |
| `archdiagram.exe` | You only ever want code analysis and don't want the detection step. |
| `archsql.exe` | Same, for SQL. |

`arch code …` and `arch sql …` are drop-in equivalents of the two standalone exes —
byte-for-byte identical output — so you rarely need the separate binaries.

`run.cmd` wraps `arch.exe`: it builds first if needed, takes a folder as an argument or a
drag-and-drop or a prompt, forwards any other options straight through, and keeps the
window open so you can read the result.

```
run.cmd C:\src\MyProject --out C:\reports\mine --no-open
```

## The commands

```
arch <path> [options]                       detect content, generate the right site(s)
arch code <path> [options]                  force code analysis only
arch sql  <path> [options]                  force SQL analysis only
arch connect (--conn-file <p> | --env) …    read a live SQL Server, read-only
arch landscape <parent-dir> [options]       federate the sites already generated under a folder
arch -h | --help
```

Via `arch sql`, everything the SQL analyser offers is reachable:

```
arch sql --from-model <model.json> [--out <dir>]      rebuild a site from a saved model
arch sql --format <file-or-folder> [--check] [--dialect <d>]   format SQL (--check = verify only)
arch sql impact <schema.object> [--model <model.json>]  what breaks if this changes
arch sql diff <old.json> <new.json> [--out <md>] [--html <html>]
        [--fail-on breaking-change] [--baseline <f>] [--write-baseline]
```

`arch --demo` is advertised by the empty-state message but **is not implemented yet** —
it exits 2 with a note saying so.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | A crash, or every applicable provider failed. |
| 2 | Usage error — bad option, path isn't a directory, nothing analysable found. |
| 3 | A `--fail-on` gate tripped. **The site is still written.** |

## Options

**Code** (`arch <path>`, `arch code`, `archdiagram`):

| Option | Default | Effect |
|---|---|---|
| `--out <dir>` | `site-<folder-name>` | Where to write the site. |
| `--no-open` | opens | Don't launch a browser. Use this in scripts. |
| `--max-nodes <n>` | 60 (min 10) | Cap on nodes drawn in the static diagrams. |
| `--exclude <dirname>` | — | Skip a directory. Repeatable. |
| `--no-complexity` | shown | Drop complexity rankings and badges. |
| `--no-snippets` | shown | Drop inline source snippets. |
| `--no-wiki` | written | Skip the Confluence-format wiki export. |
| `--source-link-type <github\|gitlab\|local>` | `none` | Link code back to its host. |
| `--source-link-base <url>` | — | Repo/web base for those links. |
| `--source-link-ref <branch>` | `main` | Branch/tag/commit for web links. |
| `--descriptions <path>` | probes the source root | Authored descriptions sidecar. |
| `--fail-on <gate>[,…]` | none | CI gates: `cycles`, `layering`, `secrets`, `drift`, `scorecard`. |
| `--sarif <path>` | — | Write a SARIF 2.1.0 log for code-scanning dashboards. |

**SQL** (`arch <path>`, `arch sql`, `archsql`):

| Option | Default | Effect |
|---|---|---|
| `--out <dir>` | `site-<folder-name>` | Where to write the site. |
| `--no-open` | opens | Don't launch a browser. |
| `--max-nodes <n>` | 60 | Cap on nodes in the static diagrams. |
| `--exclude <dirname>` | — | Skip a directory. Repeatable. |
| `--exclude-pattern <glob>` | — | Drop objects whose name/id matches (`*`, `?`). Repeatable. |
| `--config <path>` | probes for `archsql.config.json` | Config file, which can also carry `excludePatterns`. |
| `--dialect <tsql\|mysql\|postgres\|auto>` | `auto` | Force the dialect instead of detecting it. |
| `--fail-on <gate>[,…]` | none | CI gates: `secrets`, `injection`, `no-pk`, `complexity`, `scorecard`. |
| `--sarif <path>` | — | Write a SARIF log. |
| `--baseline <model.json>` | — | An earlier scan to diff against; drives the Schema Diff page. |

## What you get

Single-provider run — the output folder holds:

```
index.html        the Overview; this is what opens
guide.html        how to read every other page
…                 one page per nav entry (see below)
files/            a page per source file (code sites)
assets/           site.css, site.js, vendored libs — no CDN
model.json        the whole analysis, machine-readable
ARCHITECTURE.md   a Markdown summary (code sites)
wiki/             Confluence Storage Format export (unless --no-wiki)
```

When a folder contains **both** code and SQL, the two sites are nested and a landing page
is written above them:

```
index.html        the hub: a card per site with its headline figures
code/             a complete, unmodified code site
sql/              a complete, unmodified SQL site
```

The hub also reports the **code ↔ SQL join**: every database the code connects to, badged
`verified match`, `matched by name`, or `not in this scan`, with matches linking straight
into the SQL site's object list.

`model.json` is the contract. Anything the site shows is in there, and
`arch sql --from-model` can rebuild a site from one.

## The pages

**Code site** — Start: Overview, System Brief, Guide · Structure: Structure,
Dependencies, Modules, Dependency Direction, Graph (3D), Explore · Health: Scorecard,
Refactoring, Metrics, Hotspots, Evolution · Code: Types & Members, API Surface, Call
Graph · Supply chain: Dependencies & Stack, Config & Secrets.

**SQL site** — Start: Overview, Guide, Explore · Schema: Objects, Domains, ER Diagram,
Relationships, Dependencies, 3D Graph, CRUD Matrix · Health: Lint, Scorecard, Metrics,
Impact, Activity, Indexes, Schema Diff · Reference: Config & Secrets.

**Landscape site** (`arch landscape`) — Overview, Databases, Interconnections.

Every site carries its own **Guide** page explaining each of these in context. That is the
authoritative tour; this list is just the map.

## The estate view

One `arch` run documents one folder. `arch landscape` documents **everything you have
already generated**, by cross-referencing their `model.json` files:

```
arch landscape <parent-dir> [--out <dir>] [--only <a,b,…>] [--title <text>] [--no-open]
```

```bash
arch C:\src\Orders   --out C:\reports\site-orders   --no-open
arch C:\src\Billing  --out C:\reports\site-billing  --no-open
arch landscape C:\reports
```

Each immediate subfolder of `<parent-dir>` holding a model is one node. It reads no source
— the sites are the input — so it is fast and can be re-run as often as you like.

| Option | Default | Effect |
|---|---|---|
| `--out <dir>` | `<parent-dir>/site-landscape` | Where to write the estate site. |
| `--only <a,b>` | all | Restrict to named subfolders — scope the view to one group. |
| `--title <text>` | derived | Heading for the estate site. |
| `--no-open` | opens | Don't launch a browser. |

It answers the questions a single-repo site structurally cannot:

- **Which systems share a database.** The one that matters for a change-impact or
  data-ownership conversation, and the one nobody can answer from a single repo.
- **Which repo produces the package another repo consumes**, as directed edges.
- **Which external packages are shared** across the estate — your real common dependency
  surface, and where a CVE lands.
- **Cross-service calls**, matched heuristically from client code.

Both site shapes are discovered: a single-provider site (`model.json` at the folder root)
and an `arch` combined-mode site (`code/model.json`, hub at the root). Either way the
landscape links to that folder's own `index.html`, so a combined site opens on its hub.

`<parent-dir>` defaults to the current directory, and a folder with no models yet produces
an empty-state page telling you to generate the sites first rather than an error.

## Analysing a live SQL Server

```
arch connect --conn-file <path> [--out <dir>] [--timeout <sec>] [--no-open]
             [--max-nodes <n>] [--fail-on <gate>…] [--baseline <model.json>]
arch connect --env …
```

The connection string is **never** a command-line argument — it would land in your shell
history and in process listings. Supply it one of two ways:

- `--conn-file <path>` — a plain text file whose entire contents are the connection
  string. Keep it out of source control.
- `--env` — read from the `ARCHSQL_CONNECTION` environment variable.

Connecting adds runtime facts a file scan can't know: execution statistics, index usage
and missing indexes, read from DMVs. The default output folder is `site-db-<database>`,
so scanning a second database doesn't overwrite the first.

Arch issues only `SELECT` queries and never writes — but **that is a property of Arch, not
something the server enforces**. Use a least-privilege read-only login. The tool prints
this warning itself on every connect.

## Using it in CI

```bash
arch code ./src --out ./artifacts/arch --no-open \
    --fail-on secrets,cycles --sarif ./artifacts/arch.sarif
```

`--fail-on` exits **3** when a gate trips, which is distinct from a usage error (2) and a
crash (1), so a pipeline can tell "the rules were broken" from "the tool broke". The site
is written either way — you always get the artifact that explains the failure.

`--sarif` writes the refactoring backlog and any failed scorecard signal in SARIF 2.1.0,
which GitHub code scanning and similar dashboards ingest directly.

`--baseline <model.json>` (SQL) and `arch sql diff` turn a previous run's `model.json`
into a drift report — commit the baseline, diff each build against it.

## Guarantees

- **Read-only.** Both analysers only ever read the folder they're pointed at. `arch
  connect` opens a read-only connection. Nothing is written outside the output folder.
- **Offline.** No CDN, no fonts, no telemetry, no network at generation or view time. The
  output folder works from `file://`, on an air-gapped machine, forever.
- **No secrets in the output.** Connection strings are detected and reported by *shape*;
  passwords never appear in any generated file.
- **Deterministic.** Same input, same bytes out. That's what makes the output diffable and
  the regression suite (`tools/golden.sh`) possible.

## Known limitations

- **Provider-specific flags don't degrade in combined mode.** When a folder has both code
  and SQL, every option is forwarded to *both* analysers. A flag only one understands (say
  `--dialect`, which is SQL-only) makes the other reject it, and Arch reports that
  provider as `FAILED` — misleading, since nothing really failed. **Workaround:** run
  `arch code <path>` and `arch sql <path> --dialect …` separately.
- **The verified code↔SQL join isn't reachable in one command.** Matching by *server and
  catalog* needs a live connection, and `arch connect` doesn't also scan code. Any single
  `arch <path>` run can therefore only reach the unverified, catalog-name match — which is
  labelled honestly as `matched by name` wherever it appears.
- **`arch --demo` is not implemented.** The empty-state message mentions it anyway.
- **Analysis is static and heuristic.** Imports and project references are read from
  source text; C# structure comes from syntax-only parsing with no compilation; call links
  are matched by method name and parameter count. Each site's Overview says so, and the
  Guide explains what's exact versus inferred. Treat it as a strong hint, not proof.

## Troubleshooting

**"Nothing to analyze here."** No source files and no `.sql` files were found. Usually the
path is one level off.

**"'…' is not a directory."** Arch takes a folder, not a file.

**A build fails saying a file is locked.** Something is holding the output — a running
`arch.exe`, or an IDE. Close it and rebuild. This is environmental, not a code problem.

**A provider reports `FAILED` in combined mode.** Check whether you passed a flag only the
other analyser understands — see Known limitations.

**The browser didn't open.** Open the `index.html` the tool prints on its last line. Add
`--no-open` if you never want it to try.

**The site looks unstyled.** The `assets/` folder next to `index.html` has to travel with
it. Move or zip the whole output folder, not just the HTML.

---

Contributing to Arch itself? [CLAUDE.md](CLAUDE.md) has the repo conventions,
[continue.md](continue.md) has the state of the work, and `tools/golden.sh` is the
regression net you must run before committing.
