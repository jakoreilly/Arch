// sqlfmt-tsql — a standalone T-SQL formatter extracted from Arch.Sql, packaged as a dotnet global
// tool so it can be used outside the Arch repo (e.g. as a pre-commit hook in another project).
//
// Usage: sqlfmt-tsql <path-to-file-or-folder> [--check] [--dialect <tsql|mysql|postgres>]
using Arch.Sql.Format;

return FormatRunner.Run(
    args,
    "Usage: sqlfmt-tsql <path-to-file-or-folder> [--check] [--dialect <tsql|mysql|postgres>]");
