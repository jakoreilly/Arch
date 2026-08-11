using Arch.Code.Analysis;
using Arch.Code.Graph;
using Arch.Code.Scanning;

namespace Arch.Code.Tests;

public class ComplexityMetricsTests
{
    // Exercises the public analyzer end-to-end (ComplexityMetrics is internal),
    // so these also prove the wiring in CSharpSyntaxAnalyzer.
    private static MethodInfo AnalyzeSingleMethod(string methodSource)
    {
        var content = "class C\n{\n" + methodSource + "\n}\n";
        var facts = new CSharpSyntaxAnalyzer().Analyze(
            new FileEntry("C:/x/C.cs", "C.cs", ".cs", content.Length), content);
        return facts.Types.Single().Methods.Single();
    }

    [Theory]
    [InlineData("void M(){}", 1, 0)]
    [InlineData("void M(){ if(a) X(); }", 2, 1)]
    [InlineData("void M(){ if(a){ if(b) X(); } }", 3, 3)]          // nested: 1 + (1+2)
    [InlineData("void M(){ if(a) X(); else Y(); }", 2, 2)]        // if 1 + else 1
    [InlineData("void M(){ if(a && b) X(); }", 3, 2)]             // if + && ; cog: 1 + 1
    [InlineData("int M() => a ? 1 : 2;", 2, 1)]                   // ternary
    public void Computes_expected_complexity(string method, int cyclomatic, int cognitive)
    {
        var m = AnalyzeSingleMethod(method);
        Assert.Equal(cyclomatic, m.Cyclomatic);
        Assert.Equal(cognitive, m.Cognitive);
    }

    [Fact]
    public void Records_1_based_line_span_of_the_declaration()
    {
        // Lines: 1="class C", 2="{", 3="void M()", 4="{", 5="}", 6="}"
        var content = "class C\n{\nvoid M()\n{\n}\n}\n";
        var facts = new CSharpSyntaxAnalyzer().Analyze(
            new FileEntry("C:/x/C.cs", "C.cs", ".cs", content.Length), content);
        var m = facts.Types.Single().Methods.Single();
        Assert.Equal(3, m.StartLine);
        Assert.Equal(5, m.EndLine);
    }

    [Fact]
    public void Two_calls_to_the_same_method_name_and_arity_dedupe_to_one_invocation_keeping_the_first_line()
    {
        // Wrapped by AnalyzeSingleMethod as "class C\n{\n" + this + "\n}\n":
        // line 1="class C", 2="{", 3="void M()", 4="{", 5="Foo(1);", 6="Bar();", 7="Foo(2);", 8="}"
        var m = AnalyzeSingleMethod("void M()\n{\nFoo(1);\nBar();\nFoo(2);\n}");
        var foo = Assert.Single(m.Invocations, r => r.Name == "Foo");
        Assert.Equal(1, foo.Arity);
        Assert.Equal(5, foo.Line);
    }
}
