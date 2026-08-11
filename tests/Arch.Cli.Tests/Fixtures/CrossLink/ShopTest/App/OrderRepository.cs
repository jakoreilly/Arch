namespace CrossLinkFixture;

public static class OrderRepository
{
    // Catalog "SHOPTEST" matches this fixture's own folder name ("ShopTest") by name only,
    // different case on purpose (Phase 6 DoD: a case mismatch must still join). The sql side
    // here is a file scan, so Arch.Cli's join can only verify by catalog name, not server —
    // this is the "matched by name only" branch. Carries a fake credential on purpose too, to
    // prove the generated site never renders it (Hard constraint 2).
    private const string ShopConnection = "Server=sql-test-01;Database=SHOPTEST;User Id=sa;Password=SuperSecret123;";

    // No catalog in this scan covers "OtherDb" — exercises the "not in this scan" branch.
    private const string OtherConnection = "Server=other-01;Database=OtherDb;";

    public static int TotalLength => ShopConnection.Length + OtherConnection.Length;

    // Phase 4 end-to-end fixture: a Dapper call naming the table schema.sql actually declares,
    // so the CLI acceptance run has a real DataAccessRef -> DbObject join to show, not just the
    // pre-existing DbNode -> catalog join.
    public static int CountOrders(System.Data.IDbConnection db) =>
        db.Query<int>("SELECT COUNT(*) FROM dbo.Orders").Single();
}
