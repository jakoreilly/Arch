using Arch.Code.Analysis;
using Arch.Code.Graph;

namespace Arch.Code.Tests;

public class CallGraphBuilderTests
{
    [Fact]
    public void Ambiguous_call_reports_the_real_candidate_count()
    {
        var declA = new FileNode
        {
            RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 10,
            Types = [new TypeInfo { Name = "A", Kind = "class", Methods = [new MethodInfo { Name = "Handle", Arity = 1 }] }],
        };
        var declB = new FileNode
        {
            RelPath = "src/B.cs", Slug = "b", Language = "C#", Loc = 10,
            Types = [new TypeInfo { Name = "B", Kind = "class", Methods = [new MethodInfo { Name = "Handle", Arity = 1 }] }],
        };
        var caller = new FileNode
        {
            RelPath = "src/Caller.cs", Slug = "caller", Language = "C#", Loc = 10,
            Types = [new TypeInfo
            {
                Name = "Caller", Kind = "class",
                Methods = [new MethodInfo { Name = "Run", Invocations = [new InvocationRef("Handle", 1, 7)] }],
            }],
        };

        var edges = CallGraphBuilder.Build([declA, declB, caller]);

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(2, e.CandidateCount));
        Assert.All(edges, e => Assert.True(e.Ambiguous));
        Assert.All(edges, e => Assert.Equal(7, e.CallerLine));
    }

    [Fact]
    public void Unambiguous_call_reports_candidate_count_of_one_and_the_call_site_line()
    {
        var callee = new FileNode
        {
            RelPath = "src/Callee.cs", Slug = "callee", Language = "C#", Loc = 10,
            Types = [new TypeInfo { Name = "Callee", Kind = "class", Methods = [new MethodInfo { Name = "DoWork", Arity = 0 }] }],
        };
        var caller = new FileNode
        {
            RelPath = "src/Caller.cs", Slug = "caller", Language = "C#", Loc = 10,
            Types = [new TypeInfo
            {
                Name = "Caller", Kind = "class",
                Methods = [new MethodInfo { Name = "Run", Invocations = [new InvocationRef("DoWork", 0, 12)] }],
            }],
        };

        var edges = CallGraphBuilder.Build([callee, caller]);
        var edge = Assert.Single(edges);

        Assert.Equal(1, edge.CandidateCount);
        Assert.False(edge.Ambiguous);
        Assert.Equal(12, edge.CallerLine);
    }
}
