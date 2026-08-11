using Arch.Code.Graph;

namespace Arch.Code.Analysis;

/// <summary>Flattens the per-method DataAccessRef lists CSharpSyntaxAnalyzer/
/// ComplexityMetrics already populated (during parsing, where the SQL/Dapper/EF
/// literals are actually visible) into one whole-model list, filling in the Slug/
/// TypeName/MethodName each ref left blank at parse time — mirrors how
/// CallGraphBuilder.BuildDeclaredIndex is the first place a method's declared facts
/// meet its owning file/type, not the parse step itself. No cross-file resolution;
/// pure flatten.</summary>
public static class DataAccessScanner
{
    public static List<DataAccessRef> Build(IReadOnlyList<FileNode> files)
    {
        var refs = new List<DataAccessRef>();
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                foreach (var m in type.Methods)
                {
                    foreach (var d in m.DataAccess)
                    {
                        refs.Add(d with { Slug = file.Slug, TypeName = type.Name, MethodName = m.Name });
                    }
                }
            }
        }
        return refs
            .OrderBy(r => r.Slug, StringComparer.Ordinal).ThenBy(r => r.Line)
            .ToList();
    }
}
