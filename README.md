# Arch

Two static-site generators that read a folder and explain what is in it, sharing one
core and one `arch` executable.

- **`Arch.Code`** (`archdiagram`) analyses source code — dependency graphs, modules,
  metrics, hotspots, git evolution.
- **`Arch.Sql`** (`archsql`) analyses SQL scripts, or connects to a live server — schema
  inventory, ER diagram, CRUD matrix, lint, impact analysis.
- **`Arch.Cli`** (`arch`) detects which applies to a folder and runs it; runs both and
  writes a hub page when both apply; cross-links code → SQL when a connection string in
  the code matches the SQL model's catalog. `arch landscape <parent>` federates the
  sites already generated under a folder into one estate view.

Output is a **fully offline** static site — no CDN, no network calls, opens straight
from `file://`.

## Getting started

Just want a report out of a folder? See [IDIOTS-GUIDE.md](IDIOTS-GUIDE.md) — two
double-clicks.

Full reference, options, CI usage, and the guarantees the output makes: see
[HOW-TO-USE.md](HOW-TO-USE.md).

## Building from source

```bash
dotnet build Arch.slnx --nologo     # 0 warnings, 0 errors
dotnet test  Arch.slnx --nologo     # full test suite, ~90s
bash tools/golden.sh                # byte-identical-output regression check
```

Requires .NET SDK 10.0.300 or later.

## Layout

```
src/Arch.Core/    shared HTML/page shell, model serialisation, detection, shared assets
src/Arch.Code/    the code analyser (Arch.Code.*)
src/Arch.Sql/     the SQL analyser (Arch.Sql.*)
src/Arch.Cli/     the unified `arch` executable
tests/            the test suites for each project
tools/golden.sh   the byte-identical-output regression net
```

## Principles

- **Determinism is the product.** Same input, same bytes out.
- **Read-only.** Both analysers only ever read the folder they're pointed at.
  `arch connect` opens a read-only connection. Nothing here writes to the source it
  analyses.
- **Secrets never reach the output.** Connection strings are detected and reported by
  *shape*; passwords never appear in any generated file.

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
Copyright 2026 John O'Reilly.

Apache-2.0 rather than MIT for the express patent grant (§3) and the explicit
statement that the licence conveys no trademark rights (§6). If you redistribute Arch or
a derivative, keep the `NOTICE` file with it.

The sites Arch generates are yours; Arch claims nothing over its output. Each generated
site does carry two vendored MIT-licensed JavaScript libraries, whose notices travel with
it in `assets/lib/LICENSES.txt` — leave that file in place when you publish a site.

## Third-party code

This project bundles two third-party JavaScript libraries in its generated output; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
