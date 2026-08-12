using System.Globalization;
using System.Text;
using Arch.Code.Graph;

namespace Arch.Code.Site.Pages;

/// <summary>The deployment view: what this system calls out to, what it listens on, which
/// environments it is configured for, and what it runs in. Everything else on this site
/// describes the code; this page describes how it is wired into a network — the questions an
/// ops, platform or security reviewer asks, which the dependency graph cannot answer.
///
/// Reported by shape, never by value: hosts and ports, never paths, query strings, headers or
/// credentials. Config keys are listed by name; no value is ever read out of a config file.</summary>
public static class OpsPage
{
    public static string Body(ProjectModel model)
    {
        var net = model.Network;
        var sb = new StringBuilder();

        sb.Append("<h1>Ops &amp; Network</h1>");
        sb.Append("<p class=\"lede\">How this system is wired into a network, read out of its config and "
                + "infrastructure files: the hosts it calls, the ports it listens on, the environments it is "
                + "configured for, and the images it runs in. Endpoints are reported by <strong>shape</strong> — "
                + "scheme, host and port — never full URLs, and no config value is ever read.</p>");

        if (net.IsEmpty && model.Databases.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🌐</div>"
                    + "<p>No network surface was detected. This scan reads <code>appsettings*.json</code>, "
                    + "<code>*.config</code>, <code>launchSettings.json</code>, <code>Dockerfile</code>, "
                    + "<code>docker-compose*.yml</code> and <code>.cs</code> source — a library with no "
                    + "deployment config of its own will legitimately show nothing here.</p></div>");
            return sb.ToString();
        }

        var external = net.Outbound.Where(e => !e.IsLoopback).ToList();
        var plaintext = external.Count(e => e.IsPlaintext);
        sb.Append("<div class=\"tiles\">");
        Tile(sb, external.Count.ToString("N0"), "External endpoints");
        Tile(sb, net.Listeners.Count.ToString("N0"), "Listening ports");
        Tile(sb, plaintext.ToString("N0"), "Unencrypted", plaintext > 0);
        Tile(sb, net.Environments.Count.ToString("N0"), "Environments");
        sb.Append("</div>");

        AppendEgress(sb, net);
        AppendIngress(sb, net);
        AppendEnvironmentMatrix(sb, net);
        AppendRuntime(sb, net);
        AppendDataStores(sb, model);

        sb.Append("<p class=\"note\">Static and heuristic, like the rest of this site: a URL assembled at "
                + "runtime, injected by a service mesh, or held only in a secret store or environment variable "
                + "is invisible here. Treat this as the floor of the system's network surface, not the ceiling. "
                + "XML namespace URIs, documentation links and package-registry hosts are filtered out — they "
                + "are identifiers, not endpoints.</p>");
        return sb.ToString();
    }

    // ---- Egress ----

    private static void AppendEgress(StringBuilder sb, NetworkSurfaceModel net)
    {
        var external = net.Outbound.Where(e => !e.IsLoopback).ToList();
        var loopback = net.Outbound.Where(e => e.IsLoopback).ToList();
        var plaintext = external.Count(e => e.IsPlaintext);

        sb.Append($"<h2>Outbound <span class=\"badge {(plaintext > 0 ? "warn" : "ok")}\">"
                + $"{plaintext} unencrypted</span></h2>");

        if (net.Outbound.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>No outbound endpoint was found in code or config.</p></div>");
            return;
        }

        sb.Append("<p class=\"lede\">Every host this codebase is configured to reach. This is the list a "
                + "firewall or egress-policy conversation starts from.</p>");
        AppendEndpointTable(sb, external);

        if (loopback.Count > 0)
        {
            sb.Append($"<h3>Loopback <span class=\"badge accent\">{loopback.Count}</span></h3>");
            sb.Append("<p class=\"note\">Hard-coded <code>localhost</code>-family addresses in committed "
                    + "files. These work on a developer machine and fail everywhere else, so they are usually "
                    + "either a leftover default or config that should come from the environment.</p>");
            AppendEndpointTable(sb, loopback);
        }
    }

    private static void AppendEndpointTable(StringBuilder sb, List<NetworkEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            sb.Append("<p class=\"note\">None.</p>");
            return;
        }
        sb.Append("<table class=\"grid sortable\"><thead><tr><th>Host</th><th>Scheme</th><th>Port</th>"
                + "<th>Transport</th><th>Refs</th><th>First seen</th></tr></thead><tbody>");
        foreach (var e in endpoints)
        {
            var transport = e.IsPlaintext
                ? "<span class=\"badge warn\">plaintext</span>"
                : "<span class=\"badge ok\">encrypted</span>";
            var host = e.IsPlaceholder
                ? $"{Html.Encode(e.Host)} <span class=\"badge accent\">resolved at deploy</span>"
                : Html.Encode(e.Host);
            var port = e.Port > 0 ? Port(e.Port) : "<span style=\"color:var(--text-soft)\">default</span>";
            sb.Append($"<tr><td>{host}</td><td><code>{Html.Encode(e.Scheme)}</code></td><td>{port}</td>"
                    + $"<td>{transport}</td><td>{e.ReferenceCount:N0}</td>"
                    + $"<td><code>{Html.Encode(e.Evidence)}</code></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    // ---- Ingress ----

    private static void AppendIngress(StringBuilder sb, NetworkSurfaceModel net)
    {
        sb.Append($"<h2>Listening ports <span class=\"badge accent\">{net.Listeners.Count}</span></h2>");
        if (net.Listeners.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🔌</div>"
                    + "<p>No declared listening port. Nothing in <code>launchSettings.json</code>, a "
                    + "<code>Dockerfile</code> <code>EXPOSE</code>, or a compose port mapping — normal for a "
                    + "library or a console application.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Ports this system is declared to bind. A port here that is not in your "
                + "firewall rules — or a firewall rule not here — is the discrepancy worth chasing.</p>");
        sb.Append("<table class=\"grid sortable\"><thead><tr><th>Port</th><th>Protocol</th>"
                + "<th>Declared in</th><th>File</th></tr></thead><tbody>");
        foreach (var p in net.Listeners)
        {
            sb.Append($"<tr><td>{Port(p.Port)}</td><td><code>{Html.Encode(p.Scheme)}</code></td>"
                    + $"<td><span class=\"badge\">{Html.Encode(p.Source)}</span></td>"
                    + $"<td><code>{Html.Encode(p.Evidence)}</code></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    // ---- Environment matrix ----

    /// <summary>The comparison config drift hides in: which keys each environment overlay sets.
    /// A key present in Development but missing from Production is the classic outage, and it is
    /// invisible when the files are read one at a time.</summary>
    private static void AppendEnvironmentMatrix(StringBuilder sb, NetworkSurfaceModel net)
    {
        if (net.Environments.Count == 0) { return; }

        sb.Append($"<h2>Environments <span class=\"badge accent\">{net.Environments.Count}</span></h2>");

        if (net.Environments.Count == 1)
        {
            var only = net.Environments[0];
            sb.Append($"<p class=\"note\">One configuration file only (<code>{Html.Encode(only.RelPath)}</code>, "
                    + $"{only.Keys.Count:N0} setting(s)). Nothing to compare against — no per-environment "
                    + "overlay was found.</p>");
            return;
        }

        sb.Append("<p class=\"lede\">Which settings each environment overlay declares. A key set in one "
                + "environment but not another is where config drift lives — and the gaps below are exactly "
                + "the keys that fall back to the base file at runtime.</p>");

        var envs = net.Environments;
        var allKeys = envs.SelectMany(e => e.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Wide content scrolls inside its own container, never the page (design system rule).
        sb.Append("<div class=\"matrix-wrap\">");
        sb.Append("<table class=\"grid\"><thead><tr><th>Setting</th>");
        foreach (var e in envs)
        {
            var label = e.Name.Length == 0 ? "base" : e.Name;
            sb.Append($"<th title=\"{Html.Encode(e.RelPath)}\">{Html.Encode(label)}</th>");
        }
        sb.Append("</tr></thead><tbody>");

        foreach (var key in allKeys)
        {
            var setIn = envs.Count(e => e.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));
            // Only-in-one-place keys are the interesting rows; flag them rather than making the
            // reader diff the ticks by eye.
            var keyCell = setIn == 1
                ? $"<code>{Html.Encode(key)}</code> <span class=\"badge warn\">one only</span>"
                : $"<code>{Html.Encode(key)}</code>";
            sb.Append($"<tr><td>{keyCell}</td>");
            foreach (var e in envs)
            {
                sb.Append(e.Keys.Contains(key, StringComparer.OrdinalIgnoreCase)
                    ? "<td>&#10003;</td>"
                    : "<td style=\"color:var(--text-soft)\">&middot;</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");
        sb.Append("<p class=\"note\">Key names only — no configuration value is read or stored by this scan.</p>");
    }

    // ---- Runtime ----

    private static void AppendRuntime(StringBuilder sb, NetworkSurfaceModel net)
    {
        if (net.Images.Count == 0) { return; }

        sb.Append($"<h2>Container images <span class=\"badge accent\">{net.Images.Count}</span></h2>");
        sb.Append("<p class=\"lede\">Base and sidecar images this deployment declares. An unpinned tag "
                + "(<code>latest</code>, or no tag at all) means the runtime can change without a code change.</p>");
        sb.Append("<table class=\"grid sortable\"><thead><tr><th>Image</th><th>Tag</th>"
                + "<th>Pinned</th><th>Declared in</th></tr></thead><tbody>");
        foreach (var img in net.Images)
        {
            var (name, tag) = SplitTag(img.Image);
            var pinned = tag is not ("" or "latest");
            sb.Append($"<tr><td><code>{Html.Encode(name)}</code></td>"
                    + $"<td>{(tag.Length == 0 ? "<span style=\"color:var(--text-soft)\">none</span>" : "<code>" + Html.Encode(tag) + "</code>")}</td>"
                    + $"<td><span class=\"badge {(pinned ? "ok" : "warn")}\">{(pinned ? "pinned" : "floating")}</span></td>"
                    + $"<td><code>{Html.Encode(img.Evidence)}</code></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    /// <summary>Splits "registry.example.com:5000/team/app:1.2" into image and tag. The last colon
    /// only introduces a tag when it comes after the last slash — otherwise it is a registry port.</summary>
    public static (string Image, string Tag) SplitTag(string reference)
    {
        var lastSlash = reference.LastIndexOf('/');
        var lastColon = reference.LastIndexOf(':');
        if (lastColon > lastSlash && lastColon >= 0)
        {
            return (reference[..lastColon], reference[(lastColon + 1)..]);
        }
        return (reference, "");
    }

    // ---- Data stores ----

    /// <summary>The databases the connection-string scan already found, restated here so this page
    /// is a complete answer to "what does this system connect to". Deliberately a pointer rather
    /// than a copy of the Config &amp; Secrets analysis — the credential finding lives there.</summary>
    private static void AppendDataStores(StringBuilder sb, ProjectModel model)
    {
        if (model.Databases.Count == 0) { return; }

        sb.Append($"<h2>Data stores <span class=\"badge accent\">{model.Databases.Count}</span></h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>Database</th><th>Server</th></tr></thead><tbody>");
        foreach (var db in model.Databases.OrderBy(d => d.Label, StringComparer.OrdinalIgnoreCase))
        {
            var server = db.Server.Length > 0 ? Html.Encode(db.Server) : "<span style=\"color:var(--text-soft)\">not in the connection string</span>";
            sb.Append($"<tr><td>{Html.Encode(db.Label)}</td><td>{server}</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<p class=\"note\">Credentials, embedded secrets and the config files themselves are on "
                + "<a href=\"config.html\">Config &amp; Secrets</a>.</p>");
    }

    /// <summary>A port is an identifier, not a quantity: 8443, never "8,443". Invariant culture
    /// for the reason MarkdownExporter needs it — Arch.Cli runs with InvariantGlobalization=false,
    /// so an implicit-culture format here renders differently from the standalone exe (see
    /// continue.md, Phase 5 findings).</summary>
    private static string Port(int port) => port.ToString(CultureInfo.InvariantCulture);

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{Html.Encode(num)}</div>"
                + $"<div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }
}
