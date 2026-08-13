namespace Arch.Sql.Format;

/// <summary>The file/folder-walking CLI loop, shared verbatim by the standalone sqlfmt-tsql tool
/// and Arch.Sql's own "archsql --format" verb — one implementation, two entry points. Args are
/// pre-stripped of any leading verb/flag the caller uses to route here; args[0] is always the
/// path.</summary>
public static class FormatRunner
{
    public static int Run(string[] args, string usage)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(usage);
            return 2;
        }

        var path = args[0];
        var check = args.Contains("--check");
        var dialect = "tsql";
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--dialect") { dialect = args[i + 1]; }
        }

        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories).ToList()
            : [path];

        var changed = 0;
        var unchanged = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (dialect != "tsql")
            {
                Console.Error.WriteLine($"formatting not available for {dialect} yet: {file}");
                skipped++;
                continue;
            }

            var formatted = TSqlFormatter.Format(content);
            if (formatted.Length == 0)
            {
                Console.Error.WriteLine($"skipped (could not parse): {file}");
                skipped++;
                continue;
            }
            if (TSqlFormatter.HasInlineComments(content))
            {
                Console.Error.WriteLine($"note: {file} has comment(s) inside a statement; those cannot be preserved by the formatter and were dropped. Statement-level comments are kept.");
            }

            if (formatted == content) { unchanged++; continue; }

            if (check)
            {
                Console.Error.WriteLine($"would reformat: {file}");
                changed++;
            }
            else
            {
                File.WriteAllText(file, formatted);
                changed++;
            }
        }

        Console.Error.WriteLine($"formatted: {changed} file(s), {unchanged} unchanged, {skipped} skipped (unparseable)");
        return check && changed > 0 ? 3 : 0;
    }
}
