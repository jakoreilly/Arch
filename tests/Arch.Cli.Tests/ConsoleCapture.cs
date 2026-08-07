namespace Arch.Cli.Tests;

/// <summary>Every test class that redirects Console.Error joins this collection. xunit runs
/// distinct collections in parallel but the tests *within* one collection sequentially, and
/// Console.Error is process-global: two classes calling Console.SetError concurrently would
/// interleave, and each would assert against the other's output. RunnerTests documented that
/// hazard when it was the only such class; this makes it structural instead of a comment.</summary>
[CollectionDefinition(Name)]
public sealed class ConsoleCaptureCollection
{
    public const string Name = "console-capture";
}
