namespace Arch.Code.Graph;

/// <summary>Root of everything ArchDiagram learned about a scanned folder.
/// Serialized verbatim to model.json so other tools can reuse the analysis.</summary>
public sealed record ProjectModel
{
    public required string RootName { get; init; }
    public required string SourcePath { get; init; }
    public List<FileNode> Files { get; init; } = [];
    public List<CsprojInfo> Projects { get; init; } = [];
    public List<DbNode> Databases { get; init; } = [];
    public List<DepEdge> FileDependencies { get; init; } = [];
    public List<CallEdge> Calls { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
    public Dictionary<string, int> LanguageLoc { get; init; } = [];

    /// <summary>How to link nodes back to source; null = no source configured.</summary>
    public SourceLink? SourceLink { get; init; }

    /// <summary>Author-written project overview from the descriptions sidecar (empty = none). Additive.</summary>
    public string Description { get; init; } = "";
    /// <summary>Author-written folder descriptions, keyed by source-root-relative folder path. Additive.</summary>
    public Dictionary<string, string> FolderDescriptions { get; init; } = [];
    /// <summary>Declared architectural layers (top-to-bottom) from the optional layers sidecar;
    /// empty when none. Drives the Layering page's contract check. Additive.</summary>
    public List<LayerDef> Layers { get; init; } = [];

    /// <summary>Git history summary for the scanned tree; null when the tree is not a git
    /// working copy (a dropped-in folder, or a --from-model rebuild). Drives the Evolution
    /// page and the churn/ownership fields on each <see cref="FileNode"/>. Additive.</summary>
    public GitInfo? Git { get; init; }

    /// <summary>Deployment-facing facts for the Ops &amp; Network page: egress hosts, listening
    /// ports, configuration environments and container images. Never null; empty on a codebase
    /// with no config or infrastructure files. Appended last, per the model.json convention.</summary>
    public NetworkSurfaceModel Network { get; init; } = new();

    /// <summary>Who owns the system, from the descriptions sidecar ("" = not stated). Authored,
    /// never inferred. Additive.</summary>
    public string Owner { get; init; } = "";

    /// <summary>Authored business capabilities with scanned figures rolled up against them. Empty
    /// unless the descriptions sidecar declares capabilities — a capability map cannot be inferred
    /// from source, only asserted. Additive.</summary>
    public List<CapabilityNode> Capabilities { get; init; } = [];

    /// <summary>First-party files matched by no capability path. 0 when no capabilities are
    /// declared (nothing is claimed, so nothing is missing). Additive.</summary>
    public int UnattributedFileCount { get; init; }

    /// <summary>SHA-256 of this analysis's content, stable across machines, checkout paths and
    /// commits that change nothing relevant — so a docs pipeline can compare it against the last
    /// published run and skip the publish when nothing moved. Excludes the absolute source path,
    /// git churn and diagnostics; see <see cref="Analysis.ContentHash"/> for the exact contract.
    /// "" on a model built before this field existed. Additive.</summary>
    public string ContentHash { get; init; } = "";
}

/// <summary>One business capability: what a human asserted, plus what the scan actually found
/// under the paths they attributed to it.</summary>
public sealed record CapabilityNode
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Owner { get; init; } = "";
    /// <summary>"critical" | "high" | "medium" | "low", or whatever the author wrote ("" = none).</summary>
    public string Criticality { get; init; } = "";
    /// <summary>Free text ("PII", "PCI", "public") — badged, never interpreted.</summary>
    public string DataClassification { get; init; } = "";
    public List<string> Paths { get; init; } = [];
    /// <summary>First-party files under this capability's paths. 0 means the author's paths match
    /// nothing — a stale map, and the most useful thing this page can tell them.</summary>
    public int FileCount { get; init; }
    public int Loc { get; init; }
    public int TypeCount { get; init; }
}

/// <summary>Whole-repo git facts. <see cref="Available"/> is false when git or a .git dir was
/// absent (the per-file churn fields then stay at their defaults). <see cref="Shallow"/> is true
/// for a shallow clone, where commit counts undercount real history and must be labelled as such.</summary>
public sealed record GitInfo
{
    public bool Available { get; init; }
    public bool Shallow { get; init; }
    public int TotalCommits { get; init; }
}

/// <summary>One source/config file in the scanned tree.</summary>
public sealed record FileNode
{
    public required string RelPath { get; init; }
    public required string Slug { get; init; }
    public required string Language { get; init; }
    public long SizeBytes { get; init; }
    public int Loc { get; init; }
    /// <summary>True when the file looks like automated-test code (hidden by default in the viewer). Additive.</summary>
    public bool IsTest { get; init; }
    /// <summary>True when the file looks like a vendored/third-party/minified asset (excluded from
    /// the size-based treemap so first-party code stands out). Additive.</summary>
    public bool IsVendored { get; init; }
    public string Purpose { get; set; } = "";
    public string PurposeSource { get; set; } = "";
    public List<string> Imports { get; init; } = [];
    public List<TypeInfo> Types { get; init; } = [];
    public List<TodoItem> Todos { get; init; } = [];

    /// <summary>Number of commits that touched this file (0 = unknown / no git history). A churn
    /// signal: high-churn × high-complexity files are the classic refactoring hotspots. Additive.</summary>
    public int CommitCount { get; init; }
    /// <summary>Distinct authors who touched this file (0 = unknown). AuthorCount == 1 with a
    /// non-trivial CommitCount flags a "bus factor 1" knowledge-concentration risk. Additive.</summary>
    public int AuthorCount { get; init; }
    /// <summary>The author of the most commits to this file ("" = unknown). Additive.</summary>
    public string PrincipalAuthor { get; init; } = "";
    /// <summary>ISO date (yyyy-MM-dd) of the most recent commit touching this file ("" = unknown). Additive.</summary>
    public string LastModified { get; init; } = "";

    /// <summary>SHA-256 of this file's analysed content — what its own page renders from. Lets a
    /// publisher re-upload only the file pages that actually changed. Excludes churn, authorship
    /// and last-modified date, so a commit touching a neighbour does not move it. "" on a model
    /// built before this field existed. Additive.</summary>
    public string ContentHash { get; init; } = "";
}

/// <summary>A TODO/FIXME/HACK/BUG/XXX marker found in a source comment. <see cref="Author"/>
/// is an attribution — a leading "(name)" or a "#123" ticket reference — or empty.</summary>
public sealed record TodoItem(int Line, string Tag, string Text, string Author = "");

public sealed record TypeInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string Namespace { get; init; } = "";
    public string Modifiers { get; init; } = "";
    public List<string> BaseTypes { get; init; } = [];
    public string XmlSummary { get; init; } = "";
    public List<MethodInfo> Methods { get; init; } = [];
    /// <summary>Property/indexer signatures ("Name : Type"), for the data-shape view. Additive.</summary>
    public List<string> Properties { get; init; } = [];
    /// <summary>Field signatures ("name : Type"), for the data-shape view. Additive.</summary>
    public List<string> Fields { get; init; } = [];
}

public sealed record MethodInfo
{
    public required string Name { get; init; }
    public int Arity { get; init; }
    /// <summary>Smallest legal call argument count (required, non-optional params). Additive.</summary>
    public int MinArity { get; init; }
    /// <summary>Largest legal call argument count (total params, or <see cref="int.MaxValue"/>
    /// when the last parameter is <c>params</c>). Additive; defaults track <see cref="Arity"/>.</summary>
    public int MaxArity { get; init; }
    public string Signature { get; init; } = "";
    /// <summary>Declared modifiers, e.g. "public static". Empty for implicitly-private members.
    /// Drives the public API-surface view. Additive.</summary>
    public string Modifiers { get; init; } = "";
    public string XmlSummary { get; init; } = "";
    /// <summary>Cyclomatic complexity: 1 + count of decision points (see ComplexityMetrics).</summary>
    public int Cyclomatic { get; init; }
    /// <summary>SonarSource cognitive complexity: structural + nesting increments.</summary>
    public int Cognitive { get; init; }
    /// <summary>1-based first line of the declaration in its source file (0 = unknown).</summary>
    public int StartLine { get; init; }
    /// <summary>1-based last line of the declaration in its source file (0 = unknown).</summary>
    public int EndLine { get; init; }
    public List<InvocationRef> Invocations { get; init; } = [];
}

/// <summary>A call site inside a method body: the invoked identifier + argument count.</summary>
public sealed record InvocationRef(string Name, int Arity);

/// <summary>File-to-file (or file-to-external) dependency discovered from imports.</summary>
public sealed record DepEdge
{
    public required string FromSlug { get; init; }
    /// <summary>Slug of a scanned file, or empty when the target is external.</summary>
    public string ToSlug { get; init; } = "";
    /// <summary>Package/module name when the import did not resolve to a scanned file.</summary>
    public string ExternalTarget { get; init; } = "";
}

/// <summary>Heuristic method call edge (name + arity matching; see CallGraphBuilder).</summary>
public sealed record CallEdge
{
    public required string CallerSlug { get; init; }
    public required string CallerType { get; init; }
    public required string CallerMethod { get; init; }
    public required string CalleeSlug { get; init; }
    public required string CalleeType { get; init; }
    public required string CalleeMethod { get; init; }
    public bool Ambiguous { get; init; }
}

public sealed record CsprojInfo
{
    public required string Name { get; init; }
    public required string RelPath { get; init; }
    public string TargetFramework { get; init; } = "";
    public List<string> ProjectReferenceNames { get; init; } = [];
    public List<string> PackageReferences { get; init; } = [];
    /// <summary>External NuGet references with their declared version (empty when the version is
    /// managed centrally or absent). Drives the external-dependency / version-drift view. Additive.</summary>
    public List<PackageRef> Packages { get; init; } = [];
    public List<DbUse> ConnectionStrings { get; init; } = [];
}

/// <summary>One NuGet package reference: name and declared version ("" when unknown, e.g. under
/// Central Package Management where the version lives in Directory.Packages.props).</summary>
public sealed record PackageRef(string Name, string Version);

/// <summary>A declared architectural layer: a name and the module/namespace prefixes that belong
/// to it. Layers are listed top-to-bottom (top may depend on lower, never the reverse). Loaded
/// from an optional <c>archdiagram.layers.json</c> sidecar; empty when none is provided.</summary>
public sealed record LayerDef(string Name, List<string> Namespaces);

/// <summary>A connection-string usage inside a project. Label is always
/// human-readable (catalog > variable name > short hash); the full hash lives
/// here for tooltips/metadata only.</summary>
public sealed record DbUse
{
    public required string Hash { get; init; }
    public required string Label { get; init; }
    public string Server { get; init; } = "";
    public string Catalog { get; init; } = "";
    public string VariableName { get; init; } = "";
    public string Evidence { get; init; } = "";
    /// <summary>True when the raw connection string embedded a credential (password/user id) —
    /// i.e. a secret committed to source/config. The secret value itself is never stored. Additive.</summary>
    public bool HasCredential { get; init; }
}

/// <summary>One logical database node (deduped across projects by hash).</summary>
public sealed record DbNode
{
    public required string Hash { get; init; }
    public required string Label { get; init; }
    public string Server { get; init; } = "";
    public string Catalog { get; init; } = "";
    /// <summary>Phase 6 cross-layer join outcome against an Arch.Sql model from the same
    /// combined-mode run. Null when no join was attempted — the standalone exe, single-provider
    /// Arch.Cli runs, and any run where the sql provider didn't apply all leave this null, which
    /// is what keeps their rendered output byte-identical to before Phase 6. Additive field.</summary>
    public SqlCrossLink? SqlLink { get; init; }
}

/// <summary>Phase 6: what Arch.Cli's cross-layer join found for one DbNode, set only when both a
/// code and a sql provider ran in the same invocation. See plan.md, "# Phase 6".</summary>
public sealed record SqlCrossLink
{
    /// <summary>Relative href to the matched catalog's SQL object list, or "" when Matched is
    /// false (nothing to link to).</summary>
    public required string Href { get; init; }
    /// <summary>Object count in the joined SQL model — since Arch.Sql analyzes one source per
    /// run, this is the whole catalog's object count, not a filtered subset.</summary>
    public required int ObjectCount { get; init; }
    /// <summary>True when this database was found in the sql provider's model (by a verified or
    /// unverified match); false when the sql provider ran but covered a different catalog — the
    /// "not in this scan" case.</summary>
    public required bool Matched { get; init; }
    /// <summary>True when the match is on Server+Catalog (the sql side came from a live/known
    /// connection); false when it matched by catalog name only, an unverified guess (see plan.md
    /// GOTCHA (server-name-forms)). Meaningless when Matched is false.</summary>
    public bool Verified { get; init; }
}

/// <summary>One host this system talks out to, identified by structure (scheme + host + port)
/// rather than by the full URL — paths, query strings and tokens are never captured.</summary>
public sealed record NetworkEndpoint
{
    public required string Scheme { get; init; }
    public required string Host { get; init; }
    /// <summary>0 = the scheme's default port was used (not written in the URL).</summary>
    public int Port { get; init; }
    /// <summary>First "relPath:line" this endpoint was seen at, in path order.</summary>
    public required string Evidence { get; init; }
    /// <summary>Loopback or link-local host — a configuration smell in committed config rather
    /// than a real external dependency, so it is reported but badged apart.</summary>
    public bool IsLoopback { get; init; }
    /// <summary>The host is a deploy-time token ("{serviceHost}"), so the real target is unknown
    /// to a static scan. Still a dependency; just an unresolvable one.</summary>
    public bool IsPlaceholder { get; init; }
    /// <summary>The scheme carries no transport encryption (http, ws, amqp, redis, …).</summary>
    public bool IsPlaintext { get; init; }
    /// <summary>How many places referenced this same endpoint.</summary>
    public int ReferenceCount { get; init; }
}

/// <summary>A port this system is configured to listen on, and where that was declared.</summary>
public sealed record ListeningPort
{
    public required int Port { get; init; }
    /// <summary>"http"/"https" from launchSettings, "tcp"/"udp" from a Dockerfile or compose file.</summary>
    public required string Scheme { get; init; }
    /// <summary>"launchSettings" | "Dockerfile" | "compose".</summary>
    public required string Source { get; init; }
    public required string Evidence { get; init; }
}

/// <summary>One appsettings file as a configuration environment. Only key *names* are captured —
/// never values — so a secret sitting in a config file cannot reach the output through this path.</summary>
public sealed record ConfigEnvironment
{
    /// <summary>"" for the base appsettings.json; otherwise the environment ("Production").</summary>
    public required string Name { get; init; }
    public required string RelPath { get; init; }
    /// <summary>Dotted key paths, two levels deep, sorted.</summary>
    public List<string> Keys { get; init; } = [];
}

/// <summary>A container image the deployment is built on or runs alongside.</summary>
public sealed record ContainerImage(string Image, string Evidence);

/// <summary>The deployment-facing view: egress, ingress, environments and runtime images.
/// Empty on a codebase with no config or infrastructure files, which is normal.</summary>
public sealed record NetworkSurfaceModel
{
    public List<NetworkEndpoint> Outbound { get; init; } = [];
    public List<ListeningPort> Listeners { get; init; } = [];
    public List<ConfigEnvironment> Environments { get; init; } = [];
    public List<ContainerImage> Images { get; init; } = [];

    /// <summary>Derived convenience for the page, not part of the model.json contract — without
    /// the attribute it serialises as an "isEmpty" field that restates the four lists above and
    /// would have to be kept true by any future reader.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Outbound.Count == 0 && Listeners.Count == 0
        && Environments.Count == 0 && Images.Count == 0;
}
