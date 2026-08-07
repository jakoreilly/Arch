using System.Text.Json;

namespace Arch.Code.Analysis;

/// <summary>Author-written descriptions loaded from an optional
/// <c>archdiagram.descriptions.json</c> sidecar. When present, these override the heuristic
/// <c>Purpose</c> and add an "About this project" panel; when absent, analysis falls back to the
/// generated heuristics. Paths are source-root-relative, forward-slash, case-insensitive; a key
/// ending in "/" is a folder description, otherwise an exact file.
///
/// <para>The sidecar also carries the facts no static scan can ever infer — who owns the system,
/// what business capabilities it implements, and how critical each one is. Static analysis can
/// see <c>src/Claims/</c>; it cannot know that folder is "Claims Intake, owned by the Payments
/// squad, business-critical, handles PII". Guessing that would produce confident nonsense in
/// front of a stakeholder, so it is asserted by a human and rendered as authored.</para></summary>
public sealed record AuthoredDescriptions(
    string Project,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, string> Folders)
{
    public static readonly AuthoredDescriptions Empty =
        new("", new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>Who owns the system as a whole ("" = not stated).</summary>
    public string Owner { get; init; } = "";

    /// <summary>Authored business capabilities, in the order the sidecar declares them.</summary>
    public IReadOnlyList<AuthoredCapability> Capabilities { get; init; } = [];

    public bool IsEmpty => Project.Length == 0 && Files.Count == 0 && Folders.Count == 0
        && Owner.Length == 0 && Capabilities.Count == 0;
}

/// <summary>One authored capability: a business-meaningful name, who owns it, how critical it is,
/// and the source paths that implement it. The paths are what let Arch roll real figures up
/// against a human's claim instead of just restating the prose.</summary>
public sealed record AuthoredCapability(
    string Name,
    string Description,
    string Owner,
    string Criticality,
    string DataClassification,
    IReadOnlyList<string> Paths);

public static class DescriptionsLoader
{
    public const string DefaultFileName = "archdiagram.descriptions.json";

    /// <summary>Accepted criticality values, most severe first. Anything else is kept verbatim but
    /// rendered without a severity badge — the sidecar is a human document, and rejecting a whole
    /// file over an unexpected word would be worse than showing it as written.</summary>
    public static readonly string[] CriticalityLevels = ["critical", "high", "medium", "low"];

    private sealed class Doc
    {
        public string? Project { get; set; }
        public string? Owner { get; set; }
        public Dictionary<string, string>? Files { get; set; }
        public List<CapabilityDoc>? Capabilities { get; set; }
    }

    private sealed class CapabilityDoc
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Owner { get; set; }
        public string? Criticality { get; set; }
        public string? DataClassification { get; set; }
        public List<string>? Paths { get; set; }
    }

    /// <summary>Loads descriptions from <paramref name="explicitPath"/>, or from
    /// <c>&lt;sourceRoot&gt;/archdiagram.descriptions.json</c> when null. A missing default file is
    /// normal (returns empty, no diagnostic); a missing explicit file or malformed JSON adds a
    /// diagnostic and returns empty — never throws.</summary>
    public static AuthoredDescriptions Load(string? explicitPath, string sourceRoot, List<string> diagnostics)
    {
        var path = explicitPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(sourceRoot, DefaultFileName);
            if (!File.Exists(path)) { return AuthoredDescriptions.Empty; }   // absence is normal
        }
        else if (!File.Exists(path))
        {
            diagnostics.Add($"Descriptions file not found: {path}");
            return AuthoredDescriptions.Empty;
        }

        Doc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<Doc>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Could not read descriptions file ({path}): {ex.Message}");
            return AuthoredDescriptions.Empty;
        }
        if (doc is null) { return AuthoredDescriptions.Empty; }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in doc.Files ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) { continue; }
            var norm = key.Replace('\\', '/').TrimStart('.', '/');
            if (norm.EndsWith('/')) { folders[norm.TrimEnd('/')] = value.Trim(); }
            else { files[norm] = value.Trim(); }
        }

        return new AuthoredDescriptions((doc.Project ?? "").Trim(), files, folders)
        {
            Owner = (doc.Owner ?? "").Trim(),
            Capabilities = ReadCapabilities(doc.Capabilities, diagnostics),
        };
    }

    /// <summary>Capabilities in declaration order — the author's ordering is meaningful (they tend
    /// to list the important one first) and re-sorting it would lose that. A capability with no
    /// name is skipped with a diagnostic: it cannot be rendered or matched, and silently dropping
    /// it would leave the author wondering why their edit did nothing.</summary>
    private static List<AuthoredCapability> ReadCapabilities(List<CapabilityDoc>? docs, List<string> diagnostics)
    {
        var result = new List<AuthoredCapability>();
        foreach (var (c, i) in (docs ?? []).Select((c, i) => (c, i)))
        {
            var name = (c.Name ?? "").Trim();
            if (name.Length == 0)
            {
                diagnostics.Add($"Descriptions sidecar: capability #{i + 1} has no name and was skipped.");
                continue;
            }

            var criticality = (c.Criticality ?? "").Trim().ToLowerInvariant();
            if (criticality.Length > 0 && !CriticalityLevels.Contains(criticality))
            {
                diagnostics.Add($"Descriptions sidecar: capability '{name}' has criticality "
                    + $"'{criticality}', which is not one of {string.Join("/", CriticalityLevels)}. Shown as written.");
            }

            // Normalised the same way file/folder keys are, so an author can paste a Windows path
            // or a leading "./" and it still matches.
            var paths = (c.Paths ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Replace('\\', '/').Trim().TrimStart('.', '/'))
                .Where(p => p.Length > 0)
                .ToList();

            if (paths.Count == 0)
            {
                diagnostics.Add($"Descriptions sidecar: capability '{name}' lists no paths, so no "
                    + "code could be attributed to it.");
            }

            result.Add(new AuthoredCapability(
                name,
                (c.Description ?? "").Trim(),
                (c.Owner ?? "").Trim(),
                criticality,
                (c.DataClassification ?? "").Trim(),
                paths));
        }
        return result;
    }
}
