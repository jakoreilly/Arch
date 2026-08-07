using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arch.Code.Graph;
using Arch.Code.Scanning;

namespace Arch.Code.Analysis;

/// <summary>The deployment-facing facts a network or ops reviewer asks for, extracted from files
/// the scan already walks: who this system calls out to, what it listens on, which environments
/// it is configured for, and what it runs in. None of this is inferable from the dependency graph
/// — it lives in config and infrastructure files that every other analyser here ignores.
///
/// Everything is reported by *shape*, never by value: a URL's host and port are structural facts,
/// but no query string, token, header or credential is ever read out of a file or stored. The
/// same rule the connection-string scanner follows (see ConfigSecretsPage).</summary>
public static class NetworkSurface
{
    private const long MaxScanBytes = 1024 * 1024; // same ceiling Pipeline uses for deep analysis

    /// <summary>Schemes worth reporting as an integration point. Deliberately not "any scheme":
    /// `file:`, `urn:` and `mailto:` are not network egress, and matching them adds only noise.</summary>
    private static readonly Regex UrlPattern = new(
        @"\b(?<scheme>https?|amqps?|mongodb(?:\+srv)?|redis|rediss|grpcs?|ftps?|sftp|smtps?|ws|wss|ldaps?|mssql|postgres(?:ql)?|mysql)://(?<host>[A-Za-z0-9._\-]+|\{[A-Za-z0-9._\-]+\})(?::(?<port>\d{1,5}))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Hosts that appear in source as *identifiers or documentation*, not as things the
    /// system talks to. XML namespace URIs are the big one — every .csproj and .config carries
    /// `http://schemas.microsoft.com/...`, which is a name, not an endpoint. Without this filter
    /// the page is mostly noise, and a page mostly noise does not get read.</summary>
    private static readonly string[] NonEndpointHosts =
    [
        "schemas.microsoft.com", "schemas.xmlsoap.org", "schemas.android.com", "schemas.openxmlformats.org",
        "www.w3.org", "w3.org", "www.opengis.net", "purl.org", "xmlns.com", "docbook.org",
        "docs.microsoft.com", "learn.microsoft.com", "msdn.microsoft.com", "go.microsoft.com",
        "github.com", "www.github.com", "raw.githubusercontent.com", "gitlab.com",
        "opensource.org", "www.apache.org", "creativecommons.org", "spdx.org",
        "stackoverflow.com", "developer.mozilla.org", "en.wikipedia.org",
        "www.nuget.org", "api.nuget.org", "registry.npmjs.org", "json-schema.org", "www.json.org",
        "example.com", "www.example.com", "example.org", "localhost.localdomain",
    ];

    /// <summary>Loopback and link-local names: still reported (a hard-coded localhost URL in
    /// committed config is exactly the finding an ops reviewer wants) but badged separately,
    /// because they are a configuration smell rather than a real external dependency.</summary>
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "0.0.0.0", "::1", "host.docker.internal"];

    public static NetworkSurfaceModel Analyze(string root, IReadOnlyList<FileEntry> entries, List<string> diagnostics)
    {
        var outbound = new ConcurrentBag<NetworkEndpoint>();
        var listeners = new ConcurrentBag<ListeningPort>();
        var images = new ConcurrentBag<ContainerImage>();
        var environments = new ConcurrentBag<ConfigEnvironment>();
        var problems = new ConcurrentBag<string>();

        var scannable = entries.Where(IsScannable).ToList();
        Parallel.ForEach(scannable, entry =>
        {
            string text;
            try { text = File.ReadAllText(entry.AbsPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Could not read {entry.RelPath} for the ops scan: {ex.Message}");
                return;
            }

            var name = Path.GetFileName(entry.RelPath);
            var isLaunchSettings = name.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase);

            // launchSettings URLs declare what this app *binds*, not what it calls. Feeding them
            // to the outbound scan too would report every dev port twice — once as a listener and
            // again as a loopback "dependency" on itself.
            if (!isLaunchSettings)
            {
                foreach (var e in ExtractOutbound(text, entry.RelPath)) { outbound.Add(e); }
            }
            else
            {
                foreach (var p in ExtractLaunchSettingsPorts(text, entry.RelPath)) { listeners.Add(p); }
            }
            if (IsDockerfile(name))
            {
                foreach (var p in ExtractDockerfilePorts(text, entry.RelPath)) { listeners.Add(p); }
                foreach (var i in ExtractDockerfileImages(text, entry.RelPath)) { images.Add(i); }
            }
            if (IsCompose(name))
            {
                foreach (var p in ExtractComposePorts(text, entry.RelPath)) { listeners.Add(p); }
                foreach (var i in ExtractComposeImages(text, entry.RelPath)) { images.Add(i); }
            }
            if (IsAppSettings(name))
            {
                var env = ReadEnvironment(text, entry.RelPath, name);
                if (env is not null) { environments.Add(env); }
            }
        });

        diagnostics.AddRange(problems.OrderBy(p => p, StringComparer.Ordinal));

        return new NetworkSurfaceModel
        {
            // Deduped on the structural identity (scheme+host+port), keeping the first evidence
            // in path order — the same URL in forty files is one integration point, not forty.
            Outbound = outbound
                .GroupBy(e => (e.Scheme, e.Host, e.Port))
                .Select(g => g.OrderBy(e => e.Evidence, StringComparer.OrdinalIgnoreCase).First() with
                {
                    ReferenceCount = g.Count(),
                })
                .OrderByDescending(e => e.ReferenceCount)
                .ThenBy(e => e.Host, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Port)
                .ToList(),
            Listeners = listeners
                .GroupBy(p => (p.Port, p.Scheme, p.Source))
                .Select(g => g.OrderBy(p => p.Evidence, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(p => p.Port)
                .ThenBy(p => p.Scheme, StringComparer.Ordinal)
                .ToList(),
            Environments = environments
                .OrderBy(e => e.Name == "" ? 0 : 1)   // the base appsettings.json sorts first
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.RelPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Images = images
                .GroupBy(i => i.Image, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(i => i.Evidence, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(i => i.Image, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>The files that carry deployment facts. Test code and fixtures are excluded for the
    /// same reason the connection-string scanner excludes them: a sample URL in a test is not an
    /// integration point, and including them makes a repo scanned against itself invent egress.</summary>
    private static bool IsScannable(FileEntry f)
    {
        if (TestDetection.IsTest(f.RelPath)) { return false; }
        if (f.SizeBytes > MaxScanBytes) { return false; }

        var name = Path.GetFileName(f.RelPath);
        if (IsDockerfile(name) || IsCompose(name)) { return true; }
        return f.Extension switch
        {
            ".cs" or ".config" => true,
            ".json" => IsAppSettings(name) || name.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase),
            ".yml" or ".yaml" => true,
            _ => false,
        };
    }

    private static bool IsDockerfile(string name) =>
        name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompose(string name) =>
        (name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
         || name.StartsWith("compose", StringComparison.OrdinalIgnoreCase))
        && (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

    private static bool IsAppSettings(string name) =>
        name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    // ---- Outbound endpoints ----

    public static List<NetworkEndpoint> ExtractOutbound(string text, string relPath)
    {
        var found = new List<NetworkEndpoint>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // An XML namespace declaration is a name, not an endpoint — and unlike the host
            // denylist this catches a *custom* namespace on a company domain, which would
            // otherwise look exactly like a real service call.
            if (line.Contains("xmlns", StringComparison.OrdinalIgnoreCase)) { continue; }
            if (line.Contains("targetNamespace", StringComparison.OrdinalIgnoreCase)) { continue; }

            foreach (Match m in UrlPattern.Matches(line))
            {
                var host = m.Groups["host"].Value;
                var scheme = m.Groups["scheme"].Value.ToLowerInvariant();
                if (IsNonEndpointHost(host)) { continue; }

                var port = m.Groups["port"].Success && int.TryParse(m.Groups["port"].Value, out var p) ? p : 0;
                if (port is < 0 or > 65535) { continue; }

                found.Add(new NetworkEndpoint
                {
                    Scheme = scheme,
                    Host = host,
                    Port = port,
                    Evidence = $"{relPath}:{i + 1}",
                    IsLoopback = LoopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase),
                    // A tokenised host ("{serviceHost}", "${HOST}") is resolved at deploy time, so
                    // it is a real dependency whose target this scan cannot know. Saying so is more
                    // useful than dropping it.
                    IsPlaceholder = host.StartsWith('{'),
                    IsPlaintext = scheme is "http" or "ws" or "ftp" or "smtp" or "amqp" or "redis" or "ldap" or "grpc" or "mongodb",
                    ReferenceCount = 1,
                });
            }
        }
        return found;
    }

    private static bool IsNonEndpointHost(string host) =>
        NonEndpointHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    // ---- Listening ports ----

    private static readonly Regex ApplicationUrlPattern =
        new(@"""applicationUrl""\s*:\s*""(?<urls>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ListeningPort> ExtractLaunchSettingsPorts(string text, string relPath)
    {
        var found = new List<ListeningPort>();
        foreach (Match m in ApplicationUrlPattern.Matches(text))
        {
            foreach (var url in m.Groups["urls"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var hit = UrlPattern.Match(url);
                if (!hit.Success) { continue; }
                var scheme = hit.Groups["scheme"].Value.ToLowerInvariant();
                var port = hit.Groups["port"].Success && int.TryParse(hit.Groups["port"].Value, out var p)
                    ? p
                    : scheme == "https" ? 443 : 80;
                found.Add(new ListeningPort { Port = port, Scheme = scheme, Source = "launchSettings", Evidence = relPath });
            }
        }
        return found;
    }

    private static readonly Regex ExposePattern =
        new(@"^\s*EXPOSE\s+(?<ports>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static List<ListeningPort> ExtractDockerfilePorts(string text, string relPath)
    {
        var found = new List<ListeningPort>();
        foreach (Match m in ExposePattern.Matches(text))
        {
            foreach (var token in m.Groups["ports"].Value.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                // "8080" or "8080/tcp" or "8080/udp".
                var slash = token.IndexOf('/');
                var portText = slash >= 0 ? token[..slash] : token;
                var proto = slash >= 0 ? token[(slash + 1)..].ToLowerInvariant() : "tcp";
                if (int.TryParse(portText, out var port) && port is > 0 and <= 65535)
                {
                    found.Add(new ListeningPort { Port = port, Scheme = proto, Source = "Dockerfile", Evidence = relPath });
                }
            }
        }
        return found;
    }

    private static readonly Regex FromPattern =
        new(@"^\s*FROM\s+(?<image>[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static List<ContainerImage> ExtractDockerfileImages(string text, string relPath) =>
        FromPattern.Matches(text)
            .Select(m => m.Groups["image"].Value)
            // A multi-stage build's later stages FROM a named stage, not a registry image.
            .Where(img => !img.Contains('$') && !img.Equals("scratch", StringComparison.OrdinalIgnoreCase))
            .Select(img => new ContainerImage(img, relPath))
            .ToList();

    /// <summary>Compose port mappings: `- "8080:80"`, `- 8080:80`, `- "127.0.0.1:8080:80"`. The
    /// *published* (host-side) port is the one that matters for a firewall conversation, so that
    /// is what is reported — the last-but-one field when a bind address is present.</summary>
    private static readonly Regex ComposePortPattern =
        new(@"^\s*-\s*""?(?:(?<bind>[0-9.]+):)?(?<host>\d{1,5}):(?<container>\d{1,5})(?:/(?<proto>tcp|udp))?""?\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static List<ListeningPort> ExtractComposePorts(string text, string relPath) =>
        ComposePortPattern.Matches(text)
            .Select(m => (Ok: int.TryParse(m.Groups["host"].Value, out var p), Port: p,
                          Proto: m.Groups["proto"].Success ? m.Groups["proto"].Value.ToLowerInvariant() : "tcp"))
            .Where(x => x.Ok && x.Port is > 0 and <= 65535)
            .Select(x => new ListeningPort { Port = x.Port, Scheme = x.Proto, Source = "compose", Evidence = relPath })
            .ToList();

    private static readonly Regex ComposeImagePattern =
        new(@"^\s*image:\s*""?(?<image>[^\s""#]+)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static List<ContainerImage> ExtractComposeImages(string text, string relPath) =>
        ComposeImagePattern.Matches(text)
            .Select(m => m.Groups["image"].Value)
            .Where(img => !img.Contains('$'))
            .Select(img => new ContainerImage(img, relPath))
            .ToList();

    // ---- Environments ----

    /// <summary>One appsettings file as an environment. "appsettings.json" is the base (Name "");
    /// "appsettings.Production.json" is the Production overlay. Keys are flattened to dotted paths
    /// so the page can show which environment overrides what — values are never read, only key
    /// names, so a secret sitting in a config file cannot reach the output through this path.</summary>
    public static ConfigEnvironment? ReadEnvironment(string text, string relPath, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);          // "appsettings" | "appsettings.Production"
        var dot = stem.IndexOf('.');
        var name = dot >= 0 ? stem[(dot + 1)..] : "";

        var keys = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return null; }
            CollectKeys(doc.RootElement, "", keys, depth: 0);
        }
        catch (JsonException)
        {
            // A malformed appsettings file still tells you the environment exists; the key list
            // is simply empty rather than the whole environment vanishing from the matrix.
            return new ConfigEnvironment { Name = name, RelPath = relPath, Keys = [] };
        }

        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return new ConfigEnvironment { Name = name, RelPath = relPath, Keys = keys };
    }

    /// <summary>Flattens to dotted paths, two levels deep. Deeper than that the paths get long
    /// and the matrix stops being readable; two levels is enough to distinguish
    /// "ConnectionStrings.Orders" from "Logging.LogLevel", which is the comparison being made.</summary>
    private static void CollectKeys(JsonElement element, string prefix, List<string> keys, int depth)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object && depth < 1)
            {
                CollectKeys(prop.Value, path, keys, depth + 1);
            }
            else
            {
                keys.Add(path);
            }
        }
    }
}
