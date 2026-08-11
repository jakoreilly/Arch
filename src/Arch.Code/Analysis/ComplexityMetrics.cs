// Syntax-only complexity metrics (no semantic model), matching the analyzer's
// design in CSharpSyntaxAnalyzer. Both metrics operate on the declaration node so
// they see the full method/constructor body.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arch.Code.Analysis;

/// <summary>Cyclomatic complexity (independent paths) and SonarSource cognitive
/// complexity (how tangled the control flow is) for a single method/constructor
/// declaration. Kept as small handlers so the file itself stays low-complexity.
///
/// Note: this is a pragmatic subset of the SonarSource cognitive-complexity spec.
/// It applies the nesting penalty to control-flow structures and a flat increment
/// to else clauses and boolean-operator tokens, but does NOT coalesce runs of
/// mixed &amp;&amp;/|| operators (Sonar counts alternations). The displayed severity
/// bands are wide enough that this approximation does not change a method's level.</summary>
internal static class ComplexityMetrics
{
    /// <summary>1 + one per branch-producing node. Each &amp;&amp;/||/?? operator adds a path.</summary>
    internal static int Cyclomatic(SyntaxNode body)
    {
        var count = 1;
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                // The `_ =>` default arm is not a branch, matching how the switch-*statement*
                // `default:` (DefaultSwitchLabelSyntax) is already excluded above.
                case SwitchExpressionArmSyntax arm when arm.Pattern is not DiscardPatternSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:
                    count++;
                    break;
                case BinaryExpressionSyntax b when IsShortCircuitOrCoalesce(b.OperatorToken):
                    count++;
                    break;
            }
        }
        return count;
    }

    private static bool IsShortCircuitOrCoalesce(SyntaxToken op) =>
        op.IsKind(SyntaxKind.AmpersandAmpersandToken)
        || op.IsKind(SyntaxKind.BarBarToken)
        || op.IsKind(SyntaxKind.QuestionQuestionToken);

    /// <summary>SonarSource cognitive complexity: a structural increment for each
    /// break in linear flow, plus a nesting penalty for the nesting-inducing
    /// structures. else/else-if add a flat +1 with no nesting penalty.</summary>
    internal static int Cognitive(SyntaxNode body)
    {
        var walker = new CognitiveWalker();
        walker.Visit(body);
        return walker.Score;
    }

    private sealed class CognitiveWalker : CSharpSyntaxWalker
    {
        public int Score { get; private set; }
        private int _nesting;

        // Structures that add (1 + nesting) AND increase nesting for their body.
        private void Nested(SyntaxNode node)
        {
            Score += 1 + _nesting;
            _nesting++;
            base.DefaultVisit(node);
            _nesting--;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // The `if` itself is nested-scored; a chained `else`/`else if` is a
            // flat +1 handled via VisitElseClause below.
            Score += 1 + _nesting;
            _nesting++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nesting--;
            if (node.Else is not null) { Visit(node.Else); }
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            Score += 1; // flat +1, no nesting penalty (Sonar rule)
            Visit(node.Statement);
        }

        public override void VisitForStatement(ForStatementSyntax node) => Nested(node);
        public override void VisitForEachStatement(ForEachStatementSyntax node) => Nested(node);
        public override void VisitWhileStatement(WhileStatementSyntax node) => Nested(node);
        public override void VisitDoStatement(DoStatementSyntax node) => Nested(node);
        public override void VisitSwitchStatement(SwitchStatementSyntax node) => Nested(node);
        public override void VisitCatchClause(CatchClauseSyntax node) => Nested(node);
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) => Nested(node);

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken)
             || node.OperatorToken.IsKind(SyntaxKind.BarBarToken))
            {
                Score += 1; // flat +1, no nesting change
            }
            base.VisitBinaryExpression(node);
        }
    }

    /// <summary>Cyclomatic + cognitive complexity + invocation collection in a single tree
    /// walk, for the hot path (one call per method/constructor across the whole codebase —
    /// see CSharpSyntaxAnalyzer.BuildMethod). Produces exactly the same numbers as calling
    /// <see cref="Cyclomatic"/>, <see cref="Cognitive"/> and the analyzer's invocation
    /// collector separately, verified by ComplexityMetricsTests. The rare top-level
    /// ("&lt;main&gt;") path still calls the three original methods independently — it runs
    /// once per file, not once per method, so the duplication there doesn't matter.</summary>
    internal static (int Cyclomatic, int Cognitive, List<Graph.InvocationRef> Invocations, List<Graph.DataAccessRef> DataAccess) ComputeAll(SyntaxNode body)
    {
        var walker = new CombinedWalker();
        walker.Visit(body);
        var invocations = walker.Invocations
            .DistinctBy(r => (r.Name, r.Arity))
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Arity)
            .ToList();
        // Deduped by (ObjectName, Ops), not by line: repeated identical reads of the same
        // table in one method are legitimately one fact, matching Invocations's philosophy.
        var dataAccess = walker.DataAccessRefs.DistinctBy(d => (d.ObjectName, d.Ops)).ToList();
        return (walker.Cyclomatic, walker.Cognitive, invocations, dataAccess);
    }

    /// <summary>Minimal-API route registrations (app.MapGet/MapPost/...) in a top-level
    /// statement body. Separate from <see cref="ComputeAll"/> because only the synthesized
    /// "&lt;main&gt;" method (CSharpSyntaxAnalyzer.BuildTopLevelType) ever needs this —
    /// every other caller of ComputeAll would just discard it.</summary>
    internal static List<Graph.MapCallRef> CollectMapCalls(SyntaxNode body)
    {
        var walker = new CombinedWalker();
        walker.Visit(body);
        return walker.MapCalls;
    }

    private static string InvokedName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax ma => InvokedName(ma.Name),
        GenericNameSyntax g => g.Identifier.Text,
        MemberBindingExpressionSyntax mb => InvokedName(mb.Name),
        _ => "",
    };

    /// <summary>Combines the cyclomatic switch (over every descendant, matched by kind) and
    /// the cognitive nesting walker (recursive, only over control-flow-affecting kinds) into
    /// one traversal, plus invocation collection. Mapping from the two originals:
    /// - Nodes cyclomatic counts but cognitive doesn't score individually (case labels, switch
    ///   expression arms) fall through to <see cref="DefaultVisit"/>, which does the cyclomatic-only increment.
    /// - Nodes both score (if/loops/switch-statement/catch/conditional) increment cyclomatic
    ///   inline in the same override that does the cognitive nesting.
    /// - Binary operators: &amp;&amp;/|| count for both; ?? counts for cyclomatic only (matches
    ///   the two originals, which disagree on ?? by design — see the class doc comment).
    /// - <c>SwitchStatementSyntax</c> itself is cognitive-nested but NOT a cyclomatic node
    ///   (only its case labels are), matching <see cref="Cyclomatic"/> exactly.</summary>
    private sealed class CombinedWalker : CSharpSyntaxWalker
    {
        public int Cyclomatic { get; private set; } = 1;
        public int Cognitive { get; private set; }
        public List<Graph.InvocationRef> Invocations { get; } = [];
        public List<Graph.DataAccessRef> DataAccessRefs { get; } = [];
        public List<Graph.MapCallRef> MapCalls { get; } = [];
        private readonly HashSet<SyntaxNode> _consumedLiterals = [];
        private int _nesting;

        private static readonly string[] DapperMethodNames =
            ["Query", "QueryFirst", "QueryFirstOrDefault", "QuerySingle", "Execute"];
        private static readonly string[] SqlWriteVerbs = ["INSERT INTO", "UPDATE", "DELETE FROM"];
        private static readonly (string Suffix, string Verb)[] MapVerbs =
            [("MapGet", "GET"), ("MapPost", "POST"), ("MapPut", "PUT"), ("MapDelete", "DELETE"), ("MapPatch", "PATCH")];
        // Case-SENSITIVE on purpose: real SQL embedded in a string literal is conventionally
        // written in uppercase keywords (SELECT/FROM/JOIN/...); ordinary English prose in a
        // comment or error message ("derived from the git 'origin' remote") is not. Matching
        // case-insensitively turned "from the" into a detected read of a table named "the" —
        // confirmed against this repo's own CliOptions.cs. Keyword casing is the cheapest
        // signal available without a real SQL parser, and it is the one every hand-written
        // SQL-in-C# convention already follows.
        private static readonly System.Text.RegularExpressions.Regex SqlObjectPattern = new(
            @"\b(?<kw>FROM|JOIN|INTO|UPDATE|EXEC(?:UTE)?)\s+\[?(?<obj>[\w\.\[\]]+)\]?",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(200)); // S6444: every regex here gets a timeout, matching NetworkSurface's precedent

        private void Nested(SyntaxNode node, bool cyclomaticToo)
        {
            if (cyclomaticToo) { Cyclomatic++; }
            Cognitive += 1 + _nesting;
            _nesting++;
            base.DefaultVisit(node);
            _nesting--;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            Cyclomatic++;
            Cognitive += 1 + _nesting;
            _nesting++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nesting--;
            if (node.Else is not null) { Visit(node.Else); }
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            Cognitive += 1; // flat +1, no nesting penalty (Sonar rule)
            Visit(node.Statement);
        }

        public override void VisitForStatement(ForStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitForEachStatement(ForEachStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitWhileStatement(WhileStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitDoStatement(DoStatementSyntax node) => Nested(node, cyclomaticToo: true);
        // The switch STATEMENT itself is not a cyclomatic node — only its case labels are
        // (handled in DefaultVisit below), matching Cyclomatic() exactly.
        public override void VisitSwitchStatement(SwitchStatementSyntax node) => Nested(node, cyclomaticToo: false);
        public override void VisitCatchClause(CatchClauseSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) => Nested(node, cyclomaticToo: true);

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken)
             || node.OperatorToken.IsKind(SyntaxKind.BarBarToken))
            {
                Cyclomatic++;
                Cognitive += 1; // flat +1, no nesting change
            }
            else if (node.OperatorToken.IsKind(SyntaxKind.QuestionQuestionToken))
            {
                Cyclomatic++; // cyclomatic-only, matching Cyclomatic()'s IsShortCircuitOrCoalesce
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var name = InvokedName(node.Expression);
            if (name.Length > 0) { Invocations.Add(new Graph.InvocationRef(name, node.ArgumentList.Arguments.Count, CSharpSyntaxAnalyzer.LineOf(node, first: true))); }
            TryDapperCall(name, node);
            TryStoredProcedureCall(node);
            TryMapCall(name, node);
            base.VisitInvocationExpression(node);
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression) && !_consumedLiterals.Contains(node))
            {
                var text = node.Token.ValueText;
                if (LooksLikeSql(text)) { DataAccessRefs.Add(BuildRef(text, node)); }
            }
            base.VisitLiteralExpression(node);
        }

        // Interpolated/concatenated SQL: the object name is unknowable, but the attempt is
        // not invisible — reported as a blind spot, matching Arch.Sql's CrudEntry.IsBlindSpot.
        public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var text = string.Concat(node.Contents.OfType<InterpolatedStringTextSyntax>().Select(c => c.TextToken.ValueText));
            if (LooksLikeSql(text))
            {
                DataAccessRefs.Add(new Graph.DataAccessRef
                {
                    Slug = "", TypeName = "", MethodName = "", ObjectName = "", Ops = "?",
                    Line = CSharpSyntaxAnalyzer.LineOf(node, first: true), Source = "literal", IsBlindSpot = true,
                });
            }
            base.VisitInterpolatedStringExpression(node);
        }

        private void TryDapperCall(string name, InvocationExpressionSyntax node)
        {
            if (Array.IndexOf(DapperMethodNames, name) < 0) { return; }
            var args = node.ArgumentList.Arguments;
            if (args.Count == 0 || args[0].Expression is not LiteralExpressionSyntax lit
                || !lit.IsKind(SyntaxKind.StringLiteralExpression)) { return; }
            var text = lit.Token.ValueText;
            if (!LooksLikeSql(text)) { return; }
            DataAccessRefs.Add(BuildRef(text, lit, "dapper"));
            _consumedLiterals.Add(lit);
        }

        /// <summary>CommandType.StoredProcedure as a bare identifier, paired with a
        /// string-literal command text elsewhere in the same call — e.g.
        /// <c>connection.Query("dbo.GetOrders", commandType: CommandType.StoredProcedure)</c>.
        /// Narrower than the design sketch: does not attempt the [Table]-attribute cross-type
        /// lookup EF-core detection below also skips — same-call evidence only.</summary>
        private void TryStoredProcedureCall(InvocationExpressionSyntax node)
        {
            var args = node.ArgumentList.Arguments;
            var literalArg = args.Select(a => a.Expression).OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression) && !LooksLikeSql(l.Token.ValueText) && l.Token.ValueText.Length > 0);
            if (literalArg is null || _consumedLiterals.Contains(literalArg)) { return; }
            var hasStoredProcType = args.Any(a => a.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "StoredProcedure"
                && ma.Expression.ToString().Contains("CommandType", StringComparison.Ordinal));
            if (!hasStoredProcType) { return; }
            DataAccessRefs.Add(new Graph.DataAccessRef
            {
                Slug = "", TypeName = "", MethodName = "", ObjectName = literalArg.Token.ValueText, Ops = "",
                Line = CSharpSyntaxAnalyzer.LineOf(literalArg, first: true), Source = "stored-procedure", IsBlindSpot = false,
            });
            _consumedLiterals.Add(literalArg);
        }

        /// <summary>app.MapGet("/orders/{id}", handler)-shaped minimal-API registrations.
        /// Only ever meaningful on the synthesized "&lt;main&gt;" method — see
        /// CSharpSyntaxAnalyzer.BuildTopLevelType / ComplexityMetrics.CollectMapCalls.</summary>
        private void TryMapCall(string name, InvocationExpressionSyntax node)
        {
            if (node.Expression is not MemberAccessExpressionSyntax) { return; }
            var match = MapVerbs.FirstOrDefault(v => v.Suffix == name);
            if (match.Suffix is null) { return; }
            var args = node.ArgumentList.Arguments;
            if (args.Count == 0 || args[0].Expression is not LiteralExpressionSyntax lit
                || !lit.IsKind(SyntaxKind.StringLiteralExpression)) { return; }
            MapCalls.Add(new Graph.MapCallRef(match.Verb, lit.Token.ValueText, CSharpSyntaxAnalyzer.LineOf(node, first: true)));
        }

        // Case-SENSITIVE — see SqlObjectPattern's doc comment for why (the same "from the"
        // vs. "FROM " distinction applies here, one step earlier).
        private static bool LooksLikeSql(string text) =>
            text.Contains("FROM ", StringComparison.Ordinal)
            || text.Contains("JOIN ", StringComparison.Ordinal)
            || SqlWriteVerbs.Any(v => text.Contains(v, StringComparison.Ordinal))
            || text.Contains("EXEC ", StringComparison.Ordinal);

        private Graph.DataAccessRef BuildRef(string sql, SyntaxNode node, string? sourceOverride = null)
        {
            var m = SqlObjectPattern.Match(sql);
            var obj = m.Success ? m.Groups["obj"].Value : "";
            var isExec = m.Success && m.Groups["kw"].Value.StartsWith("EXEC", StringComparison.OrdinalIgnoreCase);
            var source = isExec ? "stored-procedure" : sourceOverride ?? "literal";
            var ops = isExec ? "" :
                (sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase) ? "R" : "")
                + (sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ? "C" : "")
                + (sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) ? "U" : "")
                + (sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase) ? "D" : "");
            return new Graph.DataAccessRef
            {
                Slug = "", TypeName = "", MethodName = "", ObjectName = obj,
                Ops = obj.Length == 0 ? "?" : ops, Line = CSharpSyntaxAnalyzer.LineOf(node, first: true),
                Source = source, IsBlindSpot = obj.Length == 0,
            };
        }

        public override void DefaultVisit(SyntaxNode node)
        {
            switch (node)
            {
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                // The `_ =>` default arm is not a branch, matching Cyclomatic().
                case SwitchExpressionArmSyntax arm when arm.Pattern is not DiscardPatternSyntax:
                    Cyclomatic++;
                    break;
            }
            base.DefaultVisit(node);
        }
    }
}
