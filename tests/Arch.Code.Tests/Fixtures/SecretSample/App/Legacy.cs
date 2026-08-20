namespace SecretSampleFixture;

// A method deliberately shaped to sit at or above Severity.HighThreshold (cognitive
// complexity 11) so FilePage.AppendSnippet actually renders it, carrying a hardcoded
// password and an API-key-shaped literal — the Phase 2 end-to-end secret-scrub check
// (SecretScrubEndToEndTests) asserts neither reaches the generated HTML.
public static class Legacy
{
    public static string Build(int mode, bool retry, string env, int attempt)
    {
        var apiKey = "AKIAIOSFODNN7EXAMPLE";
        var pwd = "Sup3rS3cret!";
        var s = "";
        if (mode == 1) { s += "a"; } else if (mode == 2) { s += "b"; } else if (mode == 3) { s += "c"; } else { s += "d"; }
        for (var i = 0; i < attempt; i++)
        {
            if (retry && i % 2 == 0) { s += "r"; }
            else if (!retry && i % 3 == 0) { s += "n"; }
            else { s += "x"; }
            switch (env)
            {
                case "dev": s += "D"; break;
                case "uat": s += "U"; break;
                case "prd": s += "P"; break;
                default: s += "?"; break;
            }
            try { if (s.Contains('z')) { throw new InvalidOperationException(pwd); } }
            catch (InvalidOperationException) { s += "e"; }
        }
        return s + apiKey + pwd;
    }
}
