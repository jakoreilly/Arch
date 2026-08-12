namespace Arch.Sql.Cli;

/// <summary>Scrubs connection-string secrets out of any text before it is printed or logged.
/// Connection strings carry passwords, so a driver exception that echoes one must never reach
/// stdout/stderr intact.</summary>
public static class Redact
{
    private static readonly string[] SensitiveKeys =
    [
        "password", "pwd", "user id", "uid", "server", "data source", "address", "addr", "network address",
    ];

    /// <summary>Replaces the value of any sensitive "key=value" pair with &lt;redacted&gt;.
    /// Leaves non-connection-string text unchanged. Parses via
    /// <see cref="System.Data.Common.DbConnectionStringBuilder"/> — the same quote-aware parser
    /// <c>SqlConnectionStringBuilder</c> (see <c>ConnectOptions.cs</c>) is built on — so a
    /// semicolon embedded inside a quoted value (e.g. <c>Password="p@ss;word"</c>) does not
    /// split the value in half and leak its tail.</summary>
    public static string Message(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text; }
        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = text };
            foreach (var keyObj in builder.Keys.Cast<string>().ToList())
            {
                if (Array.IndexOf(SensitiveKeys, keyObj.ToLowerInvariant()) >= 0)
                {
                    builder[keyObj] = "<redacted>";
                }
            }
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not a well-formed connection string (e.g. a plain exception message that happens
            // to contain a ';') — fall back to a best-effort split so a malformed-but-benign
            // string is never left completely unredacted.
            return SplitRedact(text);
        }
    }

    private static string SplitRedact(string text)
    {
        var parts = text.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) { continue; }
            var key = parts[i][..eq].Trim().ToLowerInvariant();
            if (Array.IndexOf(SensitiveKeys, key) >= 0)
            {
                parts[i] = parts[i][..eq] + "=<redacted>";
            }
        }
        return string.Join(';', parts);
    }
}
