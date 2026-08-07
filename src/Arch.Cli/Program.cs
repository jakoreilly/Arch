// Arch — point it at any folder. It detects C# and SQL content and generates the right
// pages, or both. `arch code`/`arch sql` are drop-ins for the standalone archdiagram/
// archsql exes; `arch connect` reads a live SQL Server, read-only; `arch landscape`
// federates a folder of already-generated sites into one estate view.
//
// Usage: arch <path> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
//
// The verb table itself lives in Entry so it can be tested; this file is only the shell.
return Arch.Cli.Entry.Run(args);
