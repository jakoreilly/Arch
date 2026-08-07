using System.Text.Json;
using Arch.Code.Graph;

namespace Arch.Cli.Tests;

/// <summary>Covers `arch landscape` end-to-end through the real verb table (Entry.Run), including
/// the case the verb exists to serve: a parent folder holding sites that `arch` itself generated,
/// some single-provider (model.json at the root) and some combined-mode (model.json under code/).
/// Console.Error is captured, so this joins the console-capture collection alongside RunnerTests.</summary>
[Collection(ConsoleCaptureCollection.Name)]
public class LandscapeVerbTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arch-landscape-tests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private (int ExitCode, string Stderr) RunCaptured(params string[] args)
    {
        var originalError = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        try { return (Entry.Run(args), writer.ToString()); }
        finally { Console.SetError(originalError); }
    }

    /// <summary>A single-provider site as `arch <path>` writes one: model.json at the folder root.</summary>
    private void WriteSingleProviderSite(string folder, string rootName) =>
        WriteModel(Path.Combine(_root, folder), rootName);

    /// <summary>A combined-mode site as `arch <path>` writes one when a folder holds both code and
    /// SQL: each provider's complete site nested a level down, a hub at the root.</summary>
    private void WriteCombinedSite(string folder, string rootName)
    {
        var dir = Path.Combine(_root, folder);
        WriteModel(Path.Combine(dir, "code"), rootName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
    }

    private static void WriteModel(string dir, string rootName)
    {
        Directory.CreateDirectory(dir);
        var model = new ProjectModel { RootName = rootName, SourcePath = "x" };
        File.WriteAllText(Path.Combine(dir, "model.json"), JsonSerializer.Serialize(model, WriteOptions));
    }

    [Fact]
    public void Landscape_help_exits_0_and_names_the_verb()
    {
        var (exitCode, stderr) = RunCaptured("landscape", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: arch landscape <parent-dir>", stderr);
    }

    [Fact]
    public void Landscape_over_a_missing_directory_exits_2_rather_than_throwing()
    {
        var (exitCode, stderr) = RunCaptured("landscape", Path.Combine(_root, "does-not-exist"), "--no-open");

        Assert.Equal(2, exitCode);
        Assert.Contains("is not a directory", stderr);
    }

    [Fact]
    public void Landscape_federates_single_provider_and_combined_mode_sites_together()
    {
        WriteSingleProviderSite("site-orders", "Orders");
        WriteCombinedSite("site-billing", "Billing");
        var outDir = Path.Combine(_root, "site-landscape");

        var (exitCode, stderr) = RunCaptured("landscape", _root, "--out", outDir, "--no-open");

        Assert.Equal(0, exitCode);
        Assert.Contains("landscape found 2 site(s)", stderr);

        var index = File.ReadAllText(Path.Combine(outDir, "index.html"));
        Assert.Contains("Landscape — Cross-Site Overview", index);
        // Each site is reachable by its front door: the code site's index for the single-provider
        // one, the hub for the combined one — both are that folder's own index.html.
        Assert.Contains("../site-orders/index.html", index);
        Assert.Contains("../site-billing/index.html", index);
    }

    [Fact]
    public void Landscape_only_scopes_to_the_named_subset()
    {
        WriteSingleProviderSite("site-orders", "Orders");
        WriteSingleProviderSite("site-billing", "Billing");
        var outDir = Path.Combine(_root, "site-landscape");

        var (exitCode, stderr) = RunCaptured("landscape", _root, "--out", outDir, "--only", "site-orders", "--no-open");

        Assert.Equal(0, exitCode);
        Assert.Contains("landscape found 1 site(s)", stderr);
    }
}
