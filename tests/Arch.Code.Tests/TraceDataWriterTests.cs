using System.Text.Json;
using Arch.Code.Graph;
using Arch.Code.Site;

namespace Arch.Code.Tests;

public class TraceDataWriterTests
{
    [Fact]
    public void Endpoint_and_table_nodes_attach_to_their_files_with_synthetic_edges()
    {
        var model = new ProjectModel
        {
            RootName = "Sample",
            SourcePath = "C:/sample",
            Files = { new FileNode { RelPath = "src/OrdersController.cs", Slug = "orders", Language = "C#" } },
            Endpoints =
            {
                new HttpEndpoint { Verb = "GET", Route = "orders", Slug = "orders", TypeName = "OrdersController", MethodName = "Get", Source = "attribute" },
            },
            DataAccess =
            {
                new DataAccessRef { Slug = "orders", TypeName = "OrdersController", MethodName = "Get", ObjectName = "dbo.Orders", Ops = "R", Source = "literal" },
            },
        };

        var json = TraceDataWriter.BuildJson(model);
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var edges = doc.RootElement.GetProperty("edges").EnumerateArray().ToList();

        Assert.Single(nodes, n => n.GetProperty("layer").GetString() == "route");
        Assert.Single(nodes, n => n.GetProperty("layer").GetString() == "table");
        Assert.Contains(edges, e => e.GetProperty("kind").GetString() == "route"
            && e.GetProperty("target").GetString() == "orders");
        Assert.Contains(edges, e => e.GetProperty("kind").GetString() == "data-access"
            && e.GetProperty("source").GetString() == "orders");
    }

    [Fact]
    public void A_route_or_table_reference_to_a_file_outside_the_node_cap_is_dropped_not_dangling()
    {
        var files = new List<FileNode> { new FileNode { RelPath = "isolated.cs", Slug = "isolated", Language = "C#" } };
        var deps = new List<DepEdge>();
        for (var i = 0; i < GraphDataWriter.MaxNodes; i++)
        {
            files.Add(new FileNode { RelPath = $"connected/f{i}.cs", Slug = $"f{i}", Language = "C#" });
            if (i > 0) { deps.Add(new DepEdge { FromSlug = $"f{i - 1}", ToSlug = $"f{i}" }); }
        }

        var model = new ProjectModel
        {
            RootName = "Sample",
            SourcePath = "C:/sample",
            Files = files,
            FileDependencies = deps,
            Endpoints = { new HttpEndpoint { Verb = "GET", Route = "x", Slug = "isolated", TypeName = "T", MethodName = "M", Source = "attribute" } },
        };

        var json = TraceDataWriter.BuildJson(model);
        using var doc = JsonDocument.Parse(json);
        var nodeIds = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("id").GetString()).ToHashSet();

        Assert.DoesNotContain(nodeIds, id => id!.StartsWith("route:", StringComparison.Ordinal));
        foreach (var e in doc.RootElement.GetProperty("edges").EnumerateArray())
        {
            Assert.Contains(e.GetProperty("source").GetString(), nodeIds);
            Assert.Contains(e.GetProperty("target").GetString(), nodeIds);
        }
    }
}
