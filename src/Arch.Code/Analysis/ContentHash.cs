using System.Security.Cryptography;
using System.Text;
using Arch.Code.Graph;

namespace Arch.Code.Analysis;

/// <summary>Stable content fingerprints for the model and for each file in it, so a docs pipeline
/// can answer "did anything change?" without diffing generated HTML.
///
/// <para><b>What this is not.</b> It is not an incremental-generation cache. Almost every page on
/// this site is a whole-model aggregate — fan-in, the coupling matrix, hotspot ranking, the
/// scorecard — so changing one file can legitimately move the ranking of every other file, and
/// "re-render only the changed page" is not a safe transformation. Determinism is the product
/// (see CLAUDE.md); a render cache would make output depend on prior state as well as on input,
/// which is exactly what tools/golden.sh exists to prove does not happen. These hashes let a
/// <i>publisher</i> skip work. Generation always does the full job.</para>
///
/// <para><b>What is deliberately excluded.</b> Anything that varies between two runs over the
/// same source: the absolute source path (differs per machine and per checkout), git churn and
/// authorship (every commit moves it, including commits that touch nothing relevant), and
/// diagnostics (which embed absolute paths). Including any of them would produce a hash that
/// changes constantly and therefore never lets anyone skip anything. Same class of normalisation
/// tools/golden.sh applies, for the same reason.</para>
///
/// <para><b>The field list below is a contract, not a convenience.</b> It is enumerated
/// explicitly rather than derived by serialising the model, so that adding a model field does
/// not silently change every consumer's stored hash. If a new field should participate, add it
/// here on purpose — and treat that as a breaking change for anyone holding an old hash.</para></summary>
public static class ContentHash
{
    /// <summary>ASCII unit separator, delimiting every field so that ("ab","c") and ("a","bc")
    /// cannot hash alike. Written as a numeric cast rather than a character literal: 0x1F is
    /// invisible in source and does not survive an editor, a sed pass or a copy-paste reliably,
    /// and silently losing it would silently change every hash this class has ever issued.</summary>
    private const char Sep = (char)0x1F;

    /// <summary>Fingerprint of one file's analysed content — what its own page renders from.
    /// Churn, authorship and last-modified date are excluded: a commit that only touches a
    /// neighbouring file must not make this file's page look changed.</summary>
    public static string OfFile(FileNode file)
    {
        var sb = new StringBuilder();
        Append(sb, file.RelPath, file.Language, Num(file.Loc), file.SizeBytes.ToString(Invariant));
        Append(sb, file.IsTest.ToString(), file.IsVendored.ToString(), file.Purpose, file.PurposeSource);
        foreach (var import in file.Imports) { Append(sb, import); }
        foreach (var type in file.Types)
        {
            Append(sb, type.Name, type.Kind, type.Namespace, type.Modifiers);
            foreach (var b in type.BaseTypes) { Append(sb, b); }
            foreach (var p in type.Properties) { Append(sb, p); }
            foreach (var f in type.Fields) { Append(sb, f); }
            foreach (var m in type.Methods)
            {
                Append(sb, m.Name, m.Signature, m.Modifiers, Num(m.Cyclomatic), Num(m.Cognitive));
            }
        }
        foreach (var todo in file.Todos) { Append(sb, todo.Tag, todo.Text); }
        return Hash(sb);
    }

    /// <summary>Fingerprint of the whole analysis. Two runs over identical source produce the same
    /// value on any machine, from any checkout path, at any point in the repository's history.</summary>
    public static string OfModel(ProjectModel model)
    {
        var sb = new StringBuilder();

        // RootName, not SourcePath: the folder's name is content, its absolute location is not.
        Append(sb, model.RootName, model.Description, model.Owner);

        foreach (var f in model.Files) { Append(sb, OfFile(f)); }

        foreach (var p in model.Projects)
        {
            Append(sb, p.Name, p.RelPath, p.TargetFramework);
            foreach (var r in p.ProjectReferenceNames) { Append(sb, r); }
            foreach (var pkg in p.Packages) { Append(sb, pkg.Name, pkg.Version); }
            // Connection strings by their already-normalised hash — never the raw string, so a
            // fingerprint can be published anywhere without carrying a secret with it.
            foreach (var cs in p.ConnectionStrings) { Append(sb, cs.Hash, cs.HasCredential.ToString()); }
        }

        foreach (var d in model.FileDependencies) { Append(sb, d.FromSlug, d.ToSlug, d.ExternalTarget); }
        foreach (var c in model.Calls)
        {
            Append(sb, c.CallerSlug, c.CallerType, c.CallerMethod, c.CalleeSlug, c.CalleeType, c.CalleeMethod);
        }
        foreach (var db in model.Databases) { Append(sb, db.Hash, db.Label, db.Server, db.Catalog); }
        // Identity only (verb+route+handler) — Line/Source are confidence/evidence metadata,
        // not analysed content, matching the CallEdge.Ambiguous precedent (see this class's own
        // doc comment and plan.md Hard Constraint 1).
        foreach (var e in model.Endpoints) { Append(sb, e.Verb, e.Route, e.Slug, e.TypeName, e.MethodName); }
        // Identity only (object+ops+handler) — Line/Source/IsBlindSpot are confidence/evidence
        // metadata, not analysed content, matching the CallEdge.Ambiguous precedent.
        foreach (var d in model.DataAccess) { Append(sb, d.ObjectName, d.Ops, d.Slug, d.TypeName, d.MethodName); }

        foreach (var (lang, loc) in model.LanguageLoc.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Append(sb, lang, Num(loc));
        }
        foreach (var (folder, desc) in model.FolderDescriptions.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(sb, folder, desc);
        }
        foreach (var layer in model.Layers)
        {
            Append(sb, layer.Name);
            foreach (var ns in layer.Namespaces) { Append(sb, ns); }
        }

        foreach (var e in model.Network.Outbound) { Append(sb, e.Scheme, e.Host, Num(e.Port)); }
        foreach (var l in model.Network.Listeners) { Append(sb, Num(l.Port), l.Scheme, l.Source); }
        foreach (var env in model.Network.Environments)
        {
            Append(sb, env.Name, env.RelPath);
            foreach (var k in env.Keys) { Append(sb, k); }
        }
        foreach (var img in model.Network.Images) { Append(sb, img.Image); }

        foreach (var c in model.Capabilities)
        {
            Append(sb, c.Name, c.Description, c.Owner, c.Criticality, c.DataClassification);
            foreach (var p in c.Paths) { Append(sb, p); }
            Append(sb, Num(c.FileCount), Num(c.Loc));
        }

        return Hash(sb);
    }

    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Invariant, for the reason every other formatting site here needs it: Arch.Cli runs
    /// with InvariantGlobalization=false, and a hash that differed by OS locale would be worthless
    /// (see continue.md, Phase 5 findings).</summary>
    private static string Num(int value) => value.ToString(Invariant);

    private static void Append(StringBuilder sb, params string[] values)
    {
        foreach (var v in values) { sb.Append(v).Append(Sep); }
    }

    private static string Hash(StringBuilder sb) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
}
