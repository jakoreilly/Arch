using Arch.Core.Detection;
using Arch.Sql.Scanning;

namespace Arch.Sql.Cli;

/// <summary>Arch.Cli's view of this product: detect whether a path has SQL scripts, and
/// generate its site when asked. Detection reuses SqlFileScanner directly — the same
/// scanner and extension set Pipeline.BuildModel's file-scan path uses — so Detect can
/// never disagree with what a real scan would find. Does not cover `connect`: a live
/// database has no filesystem to detect against, so that path is a top-level Arch.Cli
/// verb, not something content-detection dispatches to.</summary>
public sealed class SqlAnalysisProvider : IAnalysisProvider
{
    public string Id => "sql";
    public string Describes => "SQL scripts (*.sql)";

    public Detection Detect(string sourcePath)
    {
        var diagnostics = new List<string>();
        var entries = SqlFileScanner.Scan(sourcePath, [], diagnostics);
        return entries.Count == 0
            ? new Detection(false, 0, "")
            : new Detection(true, entries.Count, $"{entries.Count} .sql files");
    }

    public object? Generate(string sourcePath, string outDir, string[] args)
    {
        var options = CliOptions.Parse(args, out _)
            ?? throw new InvalidOperationException("Arch.Cli passed unparseable args to SqlAnalysisProvider.Generate.");
        return Verbs.BuildAndGenerate(options).Model;
    }
}
