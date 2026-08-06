using Arch.Code.Analysis;
using Arch.Code.Scanning;
using Arch.Core.Detection;

namespace Arch.Code.Cli;

/// <summary>Arch.Cli's view of this product: detect whether a path looks like a codebase,
/// and generate its site when asked. Detection reuses FileSystemScanner and
/// LanguageAnalyzers.All — the exact scanner and extension set Pipeline.BuildModel itself
/// uses — so Detect can never disagree with what a real scan would find.</summary>
public sealed class CodeAnalysisProvider : IAnalysisProvider
{
    public string Id => "code";
    public string Describes => "source files (C#, TypeScript, Python, Go, Java, Rust, …)";

    public Detection Detect(string sourcePath)
    {
        var diagnostics = new List<string>();
        var entries = FileSystemScanner.Scan(sourcePath, [], diagnostics);
        var languages = entries
            .Select(e => LanguageAnalyzers.All.FirstOrDefault(a => a.CanHandle(e.Extension))?.Language)
            .Where(lang => lang is not null)
            .Select(lang => lang!)
            .ToList();
        if (languages.Count == 0) { return new Detection(false, 0, ""); }

        var summary = string.Join(", ", languages
            .GroupBy(l => l, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key} files"));
        return new Detection(true, languages.Count, summary);
    }

    public object? Generate(string sourcePath, string outDir, string[] args)
    {
        var options = CliOptions.Parse(args, out _)
            ?? throw new InvalidOperationException("Arch.Cli passed unparseable args to CodeAnalysisProvider.Generate.");
        return Verbs.BuildAndGenerate(options).Model;
    }
}
