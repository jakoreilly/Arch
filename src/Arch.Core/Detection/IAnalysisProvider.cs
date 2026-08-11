namespace Arch.Core.Detection;

/// <summary>What a provider found when it looked at a path, before committing to a full
/// scan. Cheap: a bounded directory walk, no file contents parsed.</summary>
/// <param name="Applies">True when this provider has something to contribute.</param>
/// <param name="FileCount">How many files it would analyze — used to order the
/// providers and to word the "nothing found" message.</param>
/// <param name="Summary">One line for the console, e.g. "412 C# files" or
/// "38 .sql files". Empty when Applies is false.</param>
public readonly record struct Detection(bool Applies, int FileCount, string Summary);

/// <summary>One analysis provider. Detection is cheap and always safe to call;
/// Generate is the expensive path and runs only when Detection.Applies.</summary>
public interface IAnalysisProvider
{
    /// <summary>Stable id used in --only/--skip flags and as the provider's asset
    /// subdirectory suffix ("code" -> assets-code). Lowercase, no spaces.</summary>
    string Id { get; }

    /// <summary>Human-readable capability description used only in the "nothing found"
    /// empty-state message, so that message is built from what providers actually look
    /// for rather than hardcoded in the CLI. E.g. "source files (C#, TypeScript, Python,
    /// Go, Java, Rust, …)" or "SQL scripts (*.sql)".</summary>
    string Describes { get; }

    Detection Detect(string sourcePath);

    /// <summary>Runs the full pipeline and writes this provider's pages into
    /// <paramref name="outDir"/>. Returns the model for cross-provider linking
    /// (Phase 6); providers that expose nothing linkable return null.</summary>
    object? Generate(string sourcePath, string outDir, string[] args);

    /// <summary>Every CLI flag this provider's own arg parser recognizes, mapped to whether
    /// it consumes a following value. In combined mode (both providers apply) the same argv
    /// reaches every provider, so a flag only one of them understands must not reach the
    /// other's parser — Runner uses this to filter argv down to what fits before calling
    /// <see cref="Generate"/>.</summary>
    IReadOnlyDictionary<string, bool> KnownFlags { get; }
}
