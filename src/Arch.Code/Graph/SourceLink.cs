namespace Arch.Code.Graph;

/// <summary>How to turn a repo-relative file path (+ optional line) into a
/// clickable source URL. Serialized into model.json so the offline viewer can
/// build links client-side; null when the user configured no source.</summary>
public sealed record SourceLink
{
    /// <summary>"github" | "gitlab" | "vscode" | "local" | "none".</summary>
    public required string Type { get; init; }

    /// <summary>Repo/web base ("https://github.com/org/repo") or a local root
    /// ("C:/src/app" or "file:///C:/src/app"). No trailing slash required.</summary>
    public string Base { get; init; } = "";

    /// <summary>Branch/tag/commit for web hosts; ignored for local.</summary>
    public string Ref { get; init; } = "main";

    /// <summary>Path from the REPOSITORY root down to the scanned root, with a trailing slash
    /// ("tests/Fixtures/SampleRepo/"), or "" when they are the same directory. File paths in the
    /// model are relative to the SCAN root, so without this a scan of a subfolder produces blob
    /// URLs that 404 — the same scan-root-vs-repo-root mismatch GitHistory.Analyze already
    /// corrects for churn data. Additive.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Builds a URL for a repo-relative path (forward slashes) and an
    /// optional 1-based line (0 = no line anchor). Pure + deterministic; unit-tested.
    /// Returns "" when no usable link can be formed.</summary>
    public string UrlFor(string relPath, int line = 0)
    {
        if (string.IsNullOrWhiteSpace(Base) || string.IsNullOrWhiteSpace(relPath)) { return ""; }
        // Prefix applies to the WEB hosts only: their URL is rooted at the repository, while a
        // local/vscode Base is already the scanned root itself.
        var path = Prefix.Replace('\\', '/').TrimStart('/') + relPath.Replace('\\', '/').TrimStart('/');
        var localPath = relPath.Replace('\\', '/').TrimStart('/');
        var b = Base.TrimEnd('/');
        return Type switch
        {
            "github" => $"{b}/blob/{Ref}/{path}" + (line > 0 ? $"#L{line}" : ""),
            "gitlab" => $"{b}/-/blob/{Ref}/{path}" + (line > 0 ? $"#L{line}" : ""),
            "local" => (b.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? b : "file:///" + b.Replace(" ", "%20"))
                        + "/" + localPath, // file:// cannot deep-link a line
            // The local option that CAN deep-link a line, which is the whole point of clicking
            // through from a hotspot or a complex method. Windows drive letters go in as-is;
            // VS Code accepts "vscode://file/C:/src/app/Foo.cs:42".
            "vscode" => $"vscode://file/{b.Replace('\\', '/').TrimEnd('/').Replace(" ", "%20")}/{localPath}"
                        + (line > 0 ? $":{line}" : ""),
            _ => "",
        };
    }
}
