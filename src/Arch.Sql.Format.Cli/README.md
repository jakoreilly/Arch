# sqlfmt-tsql

A standalone, loss-safe T-SQL formatter. It parses each file with
[ScriptDom](https://www.nuget.org/packages/Microsoft.SqlServer.TransactSql.ScriptDom) and
re-emits it with consistent keyword casing and clause layout.

It was extracted from Arch.Sql's `--format` verb (see [Arch](../../README.md)) so it can be used
in any repo, independent of Arch, as a pre-commit or CI formatting check for `.sql` files.

## Guarantees

- **Never corrupts or drops SQL it doesn't understand.** A file (or statement) that fails
  to parse is passed through byte-for-byte unchanged.
- **Idempotent.** Formatting already-formatted output produces the same output again.
- **Comments between statements are preserved**, including file-header banners and
  per-object doc comments, and `GO` batch separators are kept.
- **Comments *inside* a statement cannot be preserved** — ScriptDom's generator has no
  slot for them. When this happens the file is still formatted, but a note is printed to
  stderr naming the file, and inline comments are silently dropped from that file's output
  (statement-level comments elsewhere in the same file are unaffected).

## Install

From within this repo:

```bash
dotnet pack src/Arch.Sql.Format.Cli -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg Arch.SqlFmt.Tsql
```

This installs the `sqlfmt-tsql` command globally, usable from any directory. To update
after a change, `dotnet tool update --global --add-source ./nupkg Arch.SqlFmt.Tsql`.

## Usage

```
sqlfmt-tsql <path-to-file-or-folder> [--check] [--dialect <tsql|mysql|postgres>]
```

- Given a folder, every `*.sql` file under it (recursively) is formatted in place.
- `--check` reports which files *would* change, without writing anything — exits `3` if
  any would, `0` if none would. Use this in CI.
- `--dialect` defaults to `tsql`, the only dialect currently implemented. `mysql` and
  `postgres` are accepted but currently skipped with a message per file (reserved for
  future dialect support — see `ISqlDialectAnalyzer` back in the main Arch repo).

Exit codes: `0` formatted/unchanged cleanly, `2` bad usage (missing path), `3` `--check`
found files that would change.

## Extracting this tool into its own repo

Both `src/Arch.Sql.Format/` (the formatter library) and `src/Arch.Sql.Format.Cli/` (this
CLI) are self-contained: neither has a project reference to any other part of Arch, and
between them their only dependency is the `Microsoft.SqlServer.TransactSql.ScriptDom`
NuGet package. To pluck them out:

1. Copy both folders into a new location (or new repo).
2. `dotnet build src/Arch.Sql.Format.Cli/Arch.Sql.Format.Cli.csproj` — no solution file or
   sibling Arch projects required.

The one thing that stays behind is the *test* project
(`tests/Arch.Sql.Format.Tests`) — one of its tests (`Format_SemanticRoundTrip_ObjectSetUnchanged`)
cross-checks formatted output against Arch's own T-SQL analyzer, so it references
`Arch.Sql` and would need trimming or dropping if extracted standalone.
