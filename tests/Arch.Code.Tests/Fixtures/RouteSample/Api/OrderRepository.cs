namespace Api;

// Fixture source for DataAccessScanner. One of each detectable pattern, plus one
// deliberate blind spot.
public class OrderRepository
{
    private readonly System.Data.IDbConnection _db = null!;

    public int CountOrders() =>
        _db.QueryFirst<int>("SELECT COUNT(*) FROM dbo.Orders");

    public void ArchiveOrder(int id) =>
        _db.Execute($"UPDATE dbo.Orders SET Archived = 1 WHERE Id = {id}"); // blind spot: interpolated
}
