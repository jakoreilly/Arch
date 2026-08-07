using Arch.Code.Graph;

namespace Arch.Code.Analysis;

/// <summary>Attributes real, scanned code to the capabilities a human asserted in the descriptions
/// sidecar. The authored half says "Claims Intake is owned by the Payments squad and lives under
/// src/Claims/"; this half counts what is actually there, so the business view carries measured
/// figures rather than restating the prose back to the reader.
///
/// It also computes the number that makes the map honest: how much first-party code is attributed
/// to no capability at all. A capability map that silently covers 20% of the system is worse than
/// no map, because it reads as complete.</summary>
public static class CapabilityRollup
{
    public static List<CapabilityNode> Build(IReadOnlyList<FileNode> files, IReadOnlyList<AuthoredCapability> authored)
    {
        if (authored.Count == 0) { return []; }

        var firstParty = files.Where(CodebaseStats.IsFirstParty).ToList();
        var nodes = new List<CapabilityNode>(authored.Count);

        foreach (var cap in authored)
        {
            // A file can belong to more than one capability: overlapping paths are the author's
            // choice to make, and silently assigning to the first match would hide the overlap.
            var matched = firstParty.Where(f => cap.Paths.Any(p => PathMatches(f.RelPath, p))).ToList();

            nodes.Add(new CapabilityNode
            {
                Name = cap.Name,
                Description = cap.Description,
                Owner = cap.Owner,
                Criticality = cap.Criticality,
                DataClassification = cap.DataClassification,
                Paths = [.. cap.Paths],
                FileCount = matched.Count,
                Loc = matched.Sum(f => f.Loc),
                TypeCount = matched.Sum(f => f.Types.Count),
            });
        }

        return nodes;
    }

    /// <summary>First-party files matched by no capability. Returned rather than counted so the
    /// page can name a few of them — "23 files unattributed" prompts a shrug; naming three of
    /// them prompts someone to fix the map.</summary>
    public static List<FileNode> Unattributed(IReadOnlyList<FileNode> files, IReadOnlyList<AuthoredCapability> authored)
    {
        if (authored.Count == 0) { return []; }
        var allPaths = authored.SelectMany(c => c.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return files
            .Where(CodebaseStats.IsFirstParty)
            .Where(f => !allPaths.Any(p => PathMatches(f.RelPath, p)))
            .OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A capability path is a prefix: "src/Claims" matches "src/Claims/Intake.cs" and the
    /// file "src/Claims.cs" is matched only by an exact path. The segment check stops "src/Claim"
    /// from swallowing "src/Claims/" — a silent over-attribution that would inflate a capability
    /// and shrink the unattributed figure, i.e. break the one number that keeps the map honest.</summary>
    public static bool PathMatches(string relPath, string capabilityPath)
    {
        var path = capabilityPath.TrimEnd('/');
        if (path.Length == 0) { return false; }
        if (relPath.Equals(path, StringComparison.OrdinalIgnoreCase)) { return true; }
        return relPath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
    }
}
