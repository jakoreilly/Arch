using Arch.Code;
using Arch.Code.Cli;
using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

/// <summary>End-to-end over the OpsSample fixture: a real scan through Pipeline.BuildModel, then
/// the rendered page. The unit-level extractor coverage lives in NetworkSurfaceTests; this asserts
/// the wiring — that what the extractors find survives into the model and onto the page.</summary>
public class OpsPageTests
{
    private static readonly ProjectModel Model =
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.OpsSample, Open = false });

    private static readonly string Html = OpsPage.Body(Model);

    [Fact]
    public void The_scan_finds_the_fixtures_external_endpoints()
    {
        var hosts = Model.Network.Outbound.Select(e => e.Host).ToList();

        Assert.Contains("payments.partner.example.net", hosts);
        Assert.Contains("inventory.internal.example.net", hosts);
        Assert.Contains("rabbit.internal.example.net", hosts);
        Assert.Contains("otel.example.net", hosts);
    }

    /// <summary>The documentation link in the fixture's comment must not become an endpoint —
    /// this is the filter that decides whether the page is signal or noise.</summary>
    [Fact]
    public void Documentation_links_in_the_source_never_reach_the_page()
    {
        Assert.DoesNotContain(Model.Network.Outbound, e => e.Host.Contains("microsoft.com"));
        Assert.DoesNotContain("learn.microsoft.com", Html);
    }

    [Fact]
    public void Plaintext_endpoints_are_counted_and_badged()
    {
        // http://inventory... and amqp://rabbit..., but not the loopback one (counted separately).
        var plaintext = Model.Network.Outbound.Where(e => e.IsPlaintext && !e.IsLoopback).ToList();
        Assert.Equal(2, plaintext.Count);
        Assert.Contains("2 unencrypted", Html);
        Assert.Contains("<span class=\"badge warn\">plaintext</span>", Html);
    }

    /// <summary>A launchSettings applicationUrl declares what the app binds, not what it calls.
    /// Reporting it as outbound too would list every dev port twice — once as a listener and
    /// again as a loopback dependency on itself.</summary>
    [Fact]
    public void Launch_settings_urls_are_ingress_only_and_never_counted_as_outbound()
    {
        Assert.DoesNotContain(Model.Network.Outbound, e => e.Port is 5042 or 7042);
        Assert.Contains(Model.Network.Listeners, p => p.Port == 5042 && p.Source == "launchSettings");
        Assert.Contains(Model.Network.Listeners, p => p.Port == 7042 && p.Source == "launchSettings");
    }

    [Fact]
    public void A_hard_coded_loopback_address_in_committed_config_is_reported_apart()
    {
        var loopback = Assert.Single(Model.Network.Outbound, e => e.IsLoopback);
        Assert.Equal(5005, loopback.Port);
        Assert.Contains("Loopback", Html);
    }

    [Fact]
    public void Listening_ports_come_from_all_three_declaration_sites()
    {
        var sources = Model.Network.Listeners.Select(p => p.Source).Distinct().ToList();
        Assert.Contains("launchSettings", sources);
        Assert.Contains("Dockerfile", sources);
        Assert.Contains("compose", sources);
        Assert.Contains(Model.Network.Listeners, p => p is { Port: 9090, Scheme: "udp" });
    }

    /// <summary>Ports are identifiers, not quantities: 8443, never "8,443".</summary>
    [Fact]
    public void Ports_render_without_a_thousands_separator()
    {
        Assert.Contains(">8443<", Html);
        Assert.Contains(">5672<", Html);
        Assert.DoesNotContain("8,443", Html);
        Assert.DoesNotContain("5,672", Html);
    }

    [Fact]
    public void The_environment_matrix_lists_every_overlay_and_flags_keys_set_in_only_one()
    {
        Assert.Equal(["", "Development", "Production"], Model.Network.Environments.Select(e => e.Name).Order());

        // Development-only, Production-only and base-only keys each get the "one only" flag.
        Assert.Contains("DeveloperMode.SkipAuth", Html);
        Assert.Contains("Telemetry.Endpoint", Html);
        Assert.Contains("one only", Html);
        // A key every overlay sets is not flagged as a drift risk.
        Assert.Contains("Logging.LogLevel", Html);
    }

    /// <summary>Key names only. A value sitting in a config file must not reach the model or the
    /// page through the environment matrix.</summary>
    [Fact]
    public void No_configuration_value_reaches_the_model_or_the_page()
    {
        var allKeys = Model.Network.Environments.SelectMany(e => e.Keys).ToList();
        Assert.DoesNotContain(allKeys, k => k.Contains("localhost") || k.Contains("Warning") || k.Contains("true"));
        // The BaseUrl *key* is present; its value is only ever seen by the endpoint scan, which
        // keeps the host and discards the rest of the URL.
        Assert.Contains("Inventory.BaseUrl", allKeys);
    }

    [Fact]
    public void Container_images_are_listed_with_floating_tags_flagged()
    {
        Assert.Contains(Model.Network.Images, i => i.Image == "mcr.microsoft.com/dotnet/aspnet:latest");
        Assert.Contains(Model.Network.Images, i => i.Image == "redis:7.2-alpine");
        Assert.Contains("floating", Html);
        Assert.Contains("pinned", Html);
    }

    [Fact]
    public void A_codebase_with_no_deployment_config_gets_an_honest_empty_state()
    {
        var bare = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false })
            with { Network = new NetworkSurfaceModel(), Databases = [] };

        var html = OpsPage.Body(bare);

        Assert.Contains("No network surface was detected", html);
        Assert.Contains("empty-state", html);
    }
}
