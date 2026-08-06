namespace Arch.Core.Web;

/// <summary>Copies the viewer assets that ship next to the exe into a generated site's
/// assets/ folder: the shared core tree first, then any provider tree layered on top.
/// Shared by every site generator and by the Landscape generator.</summary>
public static class SiteAssets
{
    /// <param name="outDir">The site root; assets land in <c>{outDir}/assets</c>.</param>
    /// <param name="providerSubdir">A folder under the exe's directory holding
    /// provider-specific assets (e.g. "assets-code"), copied over the shared tree.
    /// null or missing = shared assets only, which is not an error: a provider may
    /// legitimately ship none.</param>
    public static void CopyTo(string outDir, string? providerSubdir = null)
    {
        var dest = Path.Combine(outDir, "assets");
        CopyTree(Path.Combine(AppContext.BaseDirectory, "assets"), dest, required: true);
        if (providerSubdir is { Length: > 0 })
        {
            CopyTree(Path.Combine(AppContext.BaseDirectory, providerSubdir), dest, required: false);
        }
    }

    private static void CopyTree(string src, string dest, bool required)
    {
        if (!Directory.Exists(src))
        {
            // The shared tree is part of the build output; its absence means an incomplete
            // build and must be loud. A provider tree is optional and its absence is normal.
            if (required)
            {
                throw new DirectoryNotFoundException(
                    $"Viewer assets not found at '{src}' — build output is incomplete.");
            }
            return;
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
