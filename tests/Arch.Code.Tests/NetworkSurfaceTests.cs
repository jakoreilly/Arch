using Arch.Code.Analysis;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

/// <summary>The extractors are where this feature lives or dies: a network page that lists every
/// XML namespace URI in the tree is noise nobody reads, so the precision filters get as much
/// coverage as the happy path.</summary>
public class NetworkSurfaceTests
{
    // ---- Outbound endpoints ----

    [Fact]
    public void Extracts_scheme_host_and_port_from_a_url()
    {
        var found = NetworkSurface.ExtractOutbound("""var c = new Uri("https://payments.example.net:8443/v2/charge");""", "App/Pay.cs");

        var e = Assert.Single(found);
        Assert.Equal("https", e.Scheme);
        Assert.Equal("payments.example.net", e.Host);
        Assert.Equal(8443, e.Port);
        Assert.Equal("App/Pay.cs:1", e.Evidence);
        Assert.False(e.IsPlaintext);
    }

    /// <summary>The path and query are deliberately not captured — a URL's tail is where tokens and
    /// keys live, and this page is a network inventory, not a URL dump.</summary>
    [Fact]
    public void Captures_only_the_structural_part_of_a_url_never_the_path_or_query()
    {
        var found = NetworkSurface.ExtractOutbound("""url = "https://api.example.net/v1/x?apikey=SUPERSECRET";""", "a.cs");

        var e = Assert.Single(found);
        Assert.Equal("api.example.net", e.Host);
        Assert.DoesNotContain("SUPERSECRET", e.Host);
        Assert.DoesNotContain("SUPERSECRET", e.Evidence);
        Assert.DoesNotContain("apikey", e.Host);
    }

    [Fact]
    public void Port_is_zero_when_the_url_relies_on_the_scheme_default()
    {
        var e = Assert.Single(NetworkSurface.ExtractOutbound("""x = "https://api.example.net/v1";""", "a.cs"));
        Assert.Equal(0, e.Port);
    }

    [Theory]
    [InlineData("http", true)]
    [InlineData("ws", true)]
    [InlineData("amqp", true)]
    [InlineData("redis", true)]
    [InlineData("https", false)]
    [InlineData("wss", false)]
    [InlineData("amqps", false)]
    [InlineData("rediss", false)]
    public void Flags_schemes_that_carry_no_transport_encryption(string scheme, bool expectPlaintext)
    {
        var e = Assert.Single(NetworkSurface.ExtractOutbound($"""x = "{scheme}://broker.example.net/q";""", "a.cs"));
        Assert.Equal(expectPlaintext, e.IsPlaintext);
    }

    [Fact]
    public void Xml_namespace_declarations_are_not_endpoints()
    {
        // The single biggest noise source: every .csproj and .config carries one of these.
        const string csproj = """<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">""";
        Assert.Empty(NetworkSurface.ExtractOutbound(csproj, "App/App.csproj"));
    }

    /// <summary>A namespace URI on a *company* domain is indistinguishable from a service call by
    /// host alone, so the line-level xmlns filter has to catch it — the host denylist cannot.</summary>
    [Fact]
    public void A_custom_namespace_uri_on_a_company_domain_is_not_an_endpoint()
    {
        Assert.Empty(NetworkSurface.ExtractOutbound("""<config xmlns:acme="http://acme-internal.example.net/schema/v1">""", "a.config"));
        Assert.Empty(NetworkSurface.ExtractOutbound("""<xs:schema targetNamespace="http://acme-internal.example.net/x">""", "a.xsd"));
    }

    [Theory]
    [InlineData("https://github.com/acme/repo")]
    [InlineData("https://learn.microsoft.com/dotnet")]
    [InlineData("https://opensource.org/licenses/MIT")]
    [InlineData("https://api.nuget.org/v3/index.json")]
    [InlineData("http://www.w3.org/2001/XMLSchema")]
    public void Documentation_and_registry_hosts_are_filtered_out(string url)
    {
        Assert.Empty(NetworkSurface.ExtractOutbound($"// see {url}", "a.cs"));
    }

    [Fact]
    public void Loopback_addresses_are_reported_but_badged_apart()
    {
        var found = NetworkSurface.ExtractOutbound("""x = "http://localhost:5000/api";""", "a.cs");
        var e = Assert.Single(found);
        Assert.True(e.IsLoopback);
        Assert.Equal(5000, e.Port);
    }

    [Fact]
    public void A_tokenised_host_is_kept_and_marked_as_resolved_at_deploy_time()
    {
        var e = Assert.Single(NetworkSurface.ExtractOutbound("""url = "https://{serviceHost}/api";""", "a.cs"));
        Assert.True(e.IsPlaceholder);
        Assert.Equal("{serviceHost}", e.Host);
    }

    // ---- Listening ports ----

    [Fact]
    public void Reads_both_urls_out_of_a_launch_settings_application_url()
    {
        const string json = """{ "profiles": { "Web": { "applicationUrl": "https://localhost:7042;http://localhost:5042" } } }""";

        var ports = NetworkSurface.ExtractLaunchSettingsPorts(json, "App/Properties/launchSettings.json");

        Assert.Equal(2, ports.Count);
        Assert.Contains(ports, p => p is { Port: 7042, Scheme: "https" });
        Assert.Contains(ports, p => p is { Port: 5042, Scheme: "http" });
        Assert.All(ports, p => Assert.Equal("launchSettings", p.Source));
    }

    [Fact]
    public void Reads_expose_directives_including_the_protocol_suffix()
    {
        const string dockerfile = """
FROM mcr.microsoft.com/dotnet/aspnet:10.0
EXPOSE 8080
EXPOSE 9090/udp 9091
""";
        var ports = NetworkSurface.ExtractDockerfilePorts(dockerfile, "Dockerfile");

        Assert.Equal(3, ports.Count);
        Assert.Contains(ports, p => p is { Port: 8080, Scheme: "tcp" });
        Assert.Contains(ports, p => p is { Port: 9090, Scheme: "udp" });
        Assert.Contains(ports, p => p is { Port: 9091, Scheme: "tcp" });
    }

    /// <summary>The published (host-side) port is what a firewall conversation is about, so that is
    /// what is reported — including when a bind address makes it the middle field.</summary>
    [Fact]
    public void Compose_port_mappings_report_the_published_host_port()
    {
        const string compose = """
services:
  web:
    ports:
      - "8080:80"
      - 9000:9000
      - "127.0.0.1:5433:5432"
""";
        var ports = NetworkSurface.ExtractComposePorts(compose, "docker-compose.yml");

        Assert.Equal([5433, 8080, 9000], ports.Select(p => p.Port).OrderBy(p => p));
    }

    [Fact]
    public void Reads_images_from_a_dockerfile_and_skips_build_stage_aliases()
    {
        const string dockerfile = """
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM $BUILDER AS other
FROM mcr.microsoft.com/dotnet/aspnet:10.0
""";
        var images = NetworkSurface.ExtractDockerfileImages(dockerfile, "Dockerfile");

        Assert.Equal(2, images.Count);
        Assert.DoesNotContain(images, i => i.Image.Contains('$'));
    }

    // ---- Environments ----

    [Fact]
    public void An_appsettings_overlay_takes_its_environment_name_from_the_filename()
    {
        var env = NetworkSurface.ReadEnvironment("""{"Logging":{"LogLevel":"Warning"}}""",
            "App/appsettings.Production.json", "appsettings.Production.json");

        Assert.NotNull(env);
        Assert.Equal("Production", env.Name);
        Assert.Equal(["Logging.LogLevel"], env.Keys);
    }

    [Fact]
    public void The_base_appsettings_file_has_an_empty_environment_name()
    {
        var env = NetworkSurface.ReadEnvironment("""{"A":1}""", "App/appsettings.json", "appsettings.json");
        Assert.NotNull(env);
        Assert.Equal("", env.Name);
    }

    /// <summary>Key names only. A config file holding a secret must not leak it into model.json or
    /// any page through the environment matrix.</summary>
    [Fact]
    public void Only_key_names_are_captured_never_values()
    {
        var env = NetworkSurface.ReadEnvironment(
            """{"ConnectionStrings":{"Orders":"Server=db;Password=hunter2"},"ApiKey":"SUPERSECRET"}""",
            "App/appsettings.json", "appsettings.json");

        Assert.NotNull(env);
        Assert.Equal(["ApiKey", "ConnectionStrings.Orders"], env.Keys);
        Assert.DoesNotContain(env.Keys, k => k.Contains("hunter2") || k.Contains("SUPERSECRET"));
    }

    [Fact]
    public void A_malformed_config_file_still_registers_the_environment_with_no_keys()
    {
        var env = NetworkSurface.ReadEnvironment("{ not json", "App/appsettings.Staging.json", "appsettings.Staging.json");

        Assert.NotNull(env);
        Assert.Equal("Staging", env.Name);
        Assert.Empty(env.Keys);
    }

    // ---- Image tag splitting ----

    [Theory]
    [InlineData("nginx", "nginx", "")]
    [InlineData("nginx:1.27", "nginx", "1.27")]
    [InlineData("mcr.microsoft.com/dotnet/aspnet:10.0", "mcr.microsoft.com/dotnet/aspnet", "10.0")]
    // The colon before the last slash is a registry port, not a tag.
    [InlineData("registry.example.net:5000/team/app", "registry.example.net:5000/team/app", "")]
    [InlineData("registry.example.net:5000/team/app:2.1", "registry.example.net:5000/team/app", "2.1")]
    public void Splits_an_image_reference_into_name_and_tag(string reference, string expectedImage, string expectedTag)
    {
        var (image, tag) = OpsPage.SplitTag(reference);
        Assert.Equal(expectedImage, image);
        Assert.Equal(expectedTag, tag);
    }
}
