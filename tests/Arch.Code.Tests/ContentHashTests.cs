using Arch.Code;
using Arch.Code.Analysis;
using Arch.Code.Cli;
using Arch.Code.Graph;

namespace Arch.Code.Tests;

/// <summary>A content hash is only useful if it has two properties at once: it must not move when
/// nothing relevant changed (or nobody can ever skip a publish) and it must move when something
/// did (or a stale doc gets served forever). Both directions are tested here.</summary>
public class ContentHashTests
{
    private static ProjectModel Build() =>
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });

    // ---- Stability ----

    [Fact]
    public void Two_scans_of_the_same_source_produce_the_same_hash()
    {
        Assert.Equal(Build().ContentHash, Build().ContentHash);
    }

    [Fact]
    public void Every_file_gets_a_hash()
    {
        var model = Build();
        Assert.NotEmpty(model.Files);
        Assert.All(model.Files, f => Assert.Equal(64, f.ContentHash.Length));
        Assert.Equal(64, model.ContentHash.Length);
    }

    /// <summary>The whole point of excluding SourcePath: the same checkout at a different absolute
    /// location — a CI agent's workspace, another developer's machine — must fingerprint the same,
    /// or the hash can never let anyone skip anything. Verified end-to-end too: the OpsSample
    /// fixture copied to a temp workspace produced a byte-identical hash.</summary>
    [Fact]
    public void The_absolute_source_path_does_not_affect_the_hash()
    {
        var model = Build();
        var moved = model with { SourcePath = @"D:\somewhere\completely\different" };

        Assert.Equal(ContentHash.OfModel(model), ContentHash.OfModel(moved));
    }

    /// <summary>The other half of that line, and the subtle one: the folder's *name* IS content —
    /// it is rendered as the site title and in every page heading — so renaming the checked-out
    /// folder legitimately changes the published docs and must move the hash. Absolute location
    /// out, name in.</summary>
    [Fact]
    public void The_root_folder_name_does_affect_the_hash_because_it_is_rendered()
    {
        var model = Build();
        Assert.NotEqual(ContentHash.OfModel(model), ContentHash.OfModel(model with { RootName = "Renamed" }));
    }

    /// <summary>Git churn moves with every commit, including commits that touch nothing relevant.
    /// Including it would make the hash change constantly and be worthless.</summary>
    [Fact]
    public void Git_churn_and_authorship_do_not_affect_the_hash()
    {
        var model = Build();
        var churned = model with
        {
            Git = new GitInfo { Available = true, TotalCommits = 9999 },
            Files = [.. model.Files.Select(f => f with
            {
                CommitCount = f.CommitCount + 100,
                AuthorCount = 7,
                PrincipalAuthor = "Someone Else",
                LastModified = "1999-01-01",
            })],
        };

        Assert.Equal(ContentHash.OfModel(model), ContentHash.OfModel(churned));
    }

    /// <summary>Diagnostics embed absolute paths, so they are excluded for the same reason
    /// SourcePath is.</summary>
    [Fact]
    public void Diagnostics_do_not_affect_the_hash()
    {
        var model = Build();
        Assert.Equal(ContentHash.OfModel(model), ContentHash.OfModel(model with { Diagnostics = ["something happened"] }));
    }

    // ---- Sensitivity ----

    [Fact]
    public void Changing_a_files_content_changes_both_that_files_hash_and_the_models()
    {
        var model = Build();
        var target = model.Files[0];
        var edited = target with { Loc = target.Loc + 1 };

        Assert.NotEqual(ContentHash.OfFile(target), ContentHash.OfFile(edited));

        var editedModel = model with { Files = [edited, .. model.Files.Skip(1)] };
        Assert.NotEqual(ContentHash.OfModel(model), ContentHash.OfModel(editedModel));
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("language")]
    [InlineData("imports")]
    [InlineData("types")]
    [InlineData("todos")]
    public void Every_rendered_aspect_of_a_file_participates_in_its_hash(string aspect)
    {
        var f = new FileNode { RelPath = "a.cs", Slug = "a_cs", Language = "C#", Loc = 10 };
        var changed = aspect switch
        {
            "purpose" => f with { Purpose = "does a thing" },
            "language" => f with { Language = "TypeScript" },
            "imports" => f with { Imports = ["System"] },
            "types" => f with { Types = [new TypeInfo { Name = "A", Kind = "class" }] },
            "todos" => f with { Todos = [new TodoItem(1, "TODO", "fix me")] },
            _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
        };

        Assert.NotEqual(ContentHash.OfFile(f), ContentHash.OfFile(changed));
    }

    [Fact]
    public void Authored_capability_and_ownership_changes_move_the_model_hash()
    {
        var model = Build();

        Assert.NotEqual(ContentHash.OfModel(model), ContentHash.OfModel(model with { Owner = "New Team" }));
        Assert.NotEqual(ContentHash.OfModel(model),
            ContentHash.OfModel(model with { Capabilities = [new CapabilityNode { Name = "Checkout" }] }));
    }

    [Fact]
    public void Network_surface_changes_move_the_model_hash()
    {
        var model = Build();
        var withEgress = model with
        {
            Network = new NetworkSurfaceModel
            {
                Outbound = [new NetworkEndpoint { Scheme = "https", Host = "api.example.net", Evidence = "a.cs:1" }],
            },
        };

        Assert.NotEqual(ContentHash.OfModel(model), ContentHash.OfModel(withEgress));
    }

    /// <summary>Field delimiting: without a separator, ("ab","c") and ("a","bc") would hash alike
    /// and two genuinely different codebases could collide.</summary>
    [Fact]
    public void Adjacent_fields_are_delimited_so_a_boundary_shift_is_not_a_collision()
    {
        var a = new FileNode { RelPath = "ab", Slug = "s", Language = "c" };
        var b = new FileNode { RelPath = "a", Slug = "s", Language = "bc" };

        Assert.NotEqual(ContentHash.OfFile(a), ContentHash.OfFile(b));
    }

    /// <summary>A fingerprint gets published to wherever the docs pipeline stores its state, so it
    /// must not carry a secret with it — connection strings enter the hash by their already
    /// normalised hash, never as raw text.</summary>
    [Fact]
    public void A_connection_strings_raw_text_never_enters_the_hash_input()
    {
        var withSecret = new ProjectModel
        {
            RootName = "x",
            SourcePath = "x",
            Projects =
            [
                new CsprojInfo
                {
                    Name = "P", RelPath = "P/P.csproj",
                    ConnectionStrings = [new DbUse { Hash = "abc", Label = "orders", HasCredential = true }],
                },
            ],
        };

        // Same normalised hash + same credential flag => same fingerprint, whatever the label or
        // server text was; the raw string is not an input and cannot be recovered from the output.
        var relabelled = withSecret with
        {
            Projects =
            [
                withSecret.Projects[0] with
                {
                    ConnectionStrings = [new DbUse { Hash = "abc", Label = "totally different", HasCredential = true }],
                },
            ],
        };

        Assert.Equal(ContentHash.OfModel(withSecret), ContentHash.OfModel(relabelled));
    }
}
