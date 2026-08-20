namespace GateCleanFixture;

// Deliberately no connection strings and no secrets, so this fixture's "secrets" gate is
// Status.Ok rather than Status.NA (which needs at least one .csproj to be found — see
// ScorecardBuilder.BuildSecretsRow) — the "gate passes cleanly" counterpart to
// Fixtures/CrossLink/ShopTest, which carries a deliberate fake credential.
public static class Hello
{
    public static string Greet(string name) => $"Hello, {name}!";
}
