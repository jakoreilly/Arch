using Arch.Code.Analysis;
using Arch.Code.Graph;

namespace Arch.Code.Tests;

public class ScorecardBuilderTests
{
    [Fact]
    public void Passing_signals_are_graded_ok()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Files.Add(new FileNode { RelPath = "tests/ATests.cs", Slug = "at", Language = "C#", Loc = 60, IsTest = true });
        // A real (clean) .csproj so drift/secrets are actually measured, not n/a for lack of data.
        m.Projects.Add(new CsprojInfo { Name = "Api", RelPath = "Api/Api.csproj",
            Packages = [new PackageRef("Serilog", "3.0.0")] });
        var card = ScorecardBuilder.Build(m);
        // No cycles, no committed secrets, no version drift, healthy test ratio → those signals pass.
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Dependency cycles"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Credentials in source"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Package version drift"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Test-code ratio"));
    }

    [Fact]
    public void Drift_and_secrets_are_na_without_any_csproj()
    {
        // No .csproj at all (e.g. a non-.NET repo) — these signals were never checked, so
        // they must report n/a rather than a false-clean "Ok: 0".
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/a.py", Slug = "a", Language = "Python", Loc = 10 });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal(ScorecardBuilder.Status.NA, Row(card, "Package version drift"));
        Assert.Equal(ScorecardBuilder.Status.NA, Row(card, "Credentials in source"));
    }

    [Fact]
    public void Cycles_are_na_when_the_closure_is_skipped_for_size()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        for (var i = 0; i < 401; i++)
        {
            m.Files.Add(new FileNode { RelPath = $"src/F{i}.cs", Slug = $"f{i}", Language = "C#", Loc = 5,
                Types = [new TypeInfo { Name = $"F{i}", Kind = "class", Namespace = $"N{i}" }] });
        }
        var card = ScorecardBuilder.Build(m);
        Assert.Equal(ScorecardBuilder.Status.NA, Row(card, "Dependency cycles"));
    }

    [Fact]
    public void Todo_markers_in_test_files_do_not_count()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Files.Add(new FileNode { RelPath = "tests/ScannerTests.cs", Slug = "t", Language = "C#", Loc = 60, IsTest = true,
            Todos = [new TodoItem(11, "TODO", "fix the widget"), new TodoItem(14, "BUG", "overflow")] });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal("0", card.Rows.Single(r => r.Metric == "TODO / FIXME markers").Value);
    }

    private static ScorecardBuilder.Status Row(ScorecardBuilder.Card c, string metric) =>
        c.Rows.Single(r => r.Metric == metric).Status;

    [Fact]
    public void Embedded_credentials_fail_the_scorecard()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Projects.Add(new CsprojInfo { Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings = [new DbUse { Hash = "h", Label = "db", HasCredential = true }] });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal(ScorecardBuilder.Status.Fail, card.Overall);
        Assert.Contains(card.Rows, r => r.Metric == "Credentials in source" && r.Status == ScorecardBuilder.Status.Fail);
    }

    [Fact]
    public void Worst_distance_is_na_when_no_abstractions_exist()
    {
        // Two concrete modules, no interfaces → abstractness 0 everywhere. Distance is an
        // artifact and must be reported n/a, not fail — so it can't force a false "AT RISK".
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 50,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Files.Add(new FileNode { RelPath = "src/B.cs", Slug = "b", Language = "C#", Loc = 50,
            Types = [new TypeInfo { Name = "B", Kind = "class", Namespace = "NB" }] });
        m.FileDependencies.Add(new DepEdge { FromSlug = "a", ToSlug = "b" });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal(ScorecardBuilder.Status.NA, card.Rows.Single(r => r.Metric == "Worst distance (D)").Status);
    }

    [Fact]
    public void Layering_is_na_without_a_contract()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 10,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        var card = ScorecardBuilder.Build(m);
        Assert.Contains(card.Rows, r => r.Metric == "Layering violations" && r.Status == ScorecardBuilder.Status.NA);
    }
}
