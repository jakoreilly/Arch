using Arch.Sql.Format;
using Xunit;

namespace Arch.Sql.Format.Tests;

/// <summary>
/// Comments between statements inside a BEGIN...END body — a procedure, function or trigger
/// body, or a braced IF/WHILE block — used to be silently dropped, because ScriptDom regenerates
/// the whole body in one GenerateScript call with no comment slot. TSqlFormatter now splices them
/// back in, at any nesting depth, the same way it already did for top-level statements.
/// </summary>
public class Phase12_FormatBeginEndTests
{
    [Fact]
    public void Format_RetainsCommentBetweenStatementsInsideAProcedureBody()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_DoWork
            AS
            BEGIN
                -- Step 1: validate input.
                DECLARE @x INT = 1;
                -- Step 2: do the work.
                UPDATE dbo.T1 SET Name = 'x' WHERE Id = @x;
            END
            """;
        var formatted = TSqlFormatter.Format(sql);
        Assert.Contains("Step 1: validate input.", formatted);
        Assert.Contains("Step 2: do the work.", formatted);
    }

    [Fact]
    public void Format_DoesNotFlagAProcedureBodysBetweenStatementCommentsAsUnpreservable()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_DoWork
            AS
            BEGIN
                -- Step 1: validate input.
                DECLARE @x INT = 1;
            END
            """;
        Assert.False(TSqlFormatter.HasInlineComments(sql));
    }

    [Fact]
    public void Format_ProcedureBodyCommentPreservationIsIdempotent()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_DoWork
            AS
            BEGIN
                -- Step 1: validate input.
                DECLARE @x INT = 1;
                -- Step 2: do the work.
                UPDATE dbo.T1 SET Name = 'x' WHERE Id = @x;
            END
            """;
        var once = TSqlFormatter.Format(sql);
        var twice = TSqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Format_RetainsCommentsInBothLevelsOfTwoNestedBeginEndBlocks()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_Nested
            AS
            BEGIN
                -- outer step
                DECLARE @x INT = 1;
                IF @x > 0
                BEGIN
                    -- inner step
                    SET @x = @x + 1;
                END
            END
            """;
        var formatted = TSqlFormatter.Format(sql);
        Assert.Contains("outer step", formatted);
        Assert.Contains("inner step", formatted);
    }

    [Fact]
    public void Format_RetainsACommentThatIsTheOnlyThingInsideABeginEndBlock()
    {
        const string sql = "CREATE PROCEDURE dbo.usp_Stub AS BEGIN -- not implemented yet\nEND";
        Assert.Contains("not implemented yet", TSqlFormatter.Format(sql));
    }

    [Fact]
    public void Format_StillFlagsAndDropsACommentInsideAnUnbracedIfBody()
    {
        const string sql = """
            CREATE PROCEDURE dbo.usp_Unbraced
            AS
            BEGIN
                IF 1 = 1
                    -- this comment sits before an unbraced IF body
                    SELECT 1;
            END
            """;
        Assert.True(TSqlFormatter.HasInlineComments(sql));
        Assert.DoesNotContain("this comment sits before an unbraced IF body", TSqlFormatter.Format(sql));
    }

    /// <summary>
    /// Regression: bodies are marked internally, while formatting is in progress, with a numbered
    /// placeholder. Marker '1' is a literal string-prefix of marker '10', '11', ... so a nested
    /// block (assigned a low marker number, since numbering happens innermost-first) followed by
    /// ten or more unrelated sibling procedures risked one marker's splice landing on a different
    /// marker's placeholder line.
    /// </summary>
    [Fact]
    public void Format_DoesNotConfuseMarkersWhenManySiblingBlocksFollowANestedOne()
    {
        var sql = """
            CREATE PROCEDURE dbo.usp_Nested
            AS
            BEGIN
                -- nested-outer comment
                IF 1 = 1
                BEGIN
                    -- nested-inner comment
                    SELECT 0;
                END
            END
            GO

            """ + string.Concat(Enumerable.Range(1, 15).Select(i => $"""
                CREATE PROCEDURE dbo.usp_Sib{i}
                AS
                BEGIN
                    -- sibling comment {i}
                    SELECT {i};
                END
                GO

                """));

        var formatted = TSqlFormatter.Format(sql);

        Assert.DoesNotContain("ARCHFMT_BLOCK", formatted);
        Assert.Contains("nested-outer comment", formatted);
        Assert.Contains("nested-inner comment", formatted);
        for (var i = 1; i <= 15; i++)
        {
            Assert.Contains($"-- sibling comment {i}\n    SELECT {i}", formatted);
        }
    }
}
