using Arch.Sql.Cli;
using Xunit;

namespace Arch.Sql.Tests;

public class RedactTests
{
    [Fact]
    public void Message_RedactsPasswordContainingASemicolon()
    {
        // DbConnectionStringBuilder is quote-aware, so the embedded ';' inside the quoted
        // password must not split the value and leak its tail (the pre-fix Split(';') bug).
        var redacted = Redact.Message("Password=\"p@ss;word\";Server=x");
        Assert.DoesNotContain("p@ss", redacted);
        // The pre-fix bug left the un-redacted quote-terminated tail `word"` in the output —
        // "word" alone is also a substring of the (intentionally redacted) key "password".
        Assert.DoesNotContain("word\"", redacted);
    }

    [Fact]
    public void Message_RedactsSimpleKeyValuePairs()
    {
        var redacted = Redact.Message("Server=myhost;Password=secret;Database=db");
        Assert.DoesNotContain("secret", redacted);
        Assert.DoesNotContain("myhost", redacted);
        Assert.Contains("db", redacted);
    }

    [Fact]
    public void Message_LeavesNonConnectionStringTextUnchanged()
    {
        var text = "something went wrong while reading the file";
        Assert.Equal(text, Redact.Message(text));
    }

    [Fact]
    public void Message_FallsBackForMalformedText()
    {
        // An unterminated quoted value makes DbConnectionStringBuilder throw ArgumentException —
        // the split fallback must still redact the sensitive pair elsewhere in the same text.
        var redacted = Redact.Message("Password=hunter2;Server=\"unterminated");
        Assert.DoesNotContain("hunter2", redacted);
    }
}
