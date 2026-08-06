// Arch — point it at any folder. It detects C# and SQL content and generates the right
// pages, or both. `arch code`/`arch sql` are drop-ins for the standalone archdiagram/
// archsql exes; `arch connect` reads a live SQL Server, read-only.
//
// Usage: arch <path> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
using Arch.Cli;

if (args.Length > 0 && args[0] == "connect") { return Arch.Sql.Cli.Verbs.RunConnect(args); }
if (args.Length > 0 && args[0] == "code") { return Arch.Code.Cli.Verbs.Run(args[1..]); }
if (args.Length > 0 && args[0] == "sql") { return Arch.Sql.Cli.Verbs.Run(args[1..]); }
if (args.Length > 0 && args[0] == "--demo")
{
    Console.Error.WriteLine("arch: --demo is not yet available.");
    return 2;
}
return Runner.Run(args);
