namespace Arch.Cli;

/// <summary>The verb table. Lived in Program.cs's top-level statements until the landscape
/// verb was added; extracted so the dispatch itself is testable (Program.cs's synthesized
/// entry point is not reachable from a test assembly, and Arch.Cli.Tests already sees this
/// assembly's internals). Behaviour-preserving: each line below is the line it replaced.</summary>
internal static class Entry
{
    internal static int Run(string[] args)
    {
        var verb = args.Length > 0 ? args[0] : "";
        switch (verb)
        {
            case "connect":
                return Arch.Sql.Cli.Verbs.RunConnect(args);
            case "code":
                return Arch.Code.Cli.Verbs.Run(args[1..]);
            case "sql":
                return Arch.Sql.Cli.Verbs.Run(args[1..]);
            // RunLandscape reads the parent dir from args[1] and its flags from args[1..],
            // exactly the shape `archdiagram --landscape <parent> ...` already produces — so
            // the verb is a rename of that entry point, not a second implementation of it.
            case "landscape":
                return Arch.Code.Cli.Verbs.RunLandscape(["--landscape", .. args[1..]]);
            // `landscape` federates sites that already exist; `group` is the orchestration that
            // creates them. The capability ArchDiagram carried in Launch-ArchDiagram.ps1 and that
            // never came across in the migration — see continue.md.
            case "group":
                return GroupRunner.Run(args[1..]);
            case "--demo":
                Console.Error.WriteLine("arch: --demo is not yet available.");
                return 2;
            default:
                return Runner.Run(args);
        }
    }
}
