namespace Api;

// Fixture source for the EF-core DataAccessRef pattern: a DbSet property and a
// same-type method that touches it and calls SaveChanges.
public class OrderContext
{
    public DbSet<Order> Orders { get; set; } = null!;

    public void AddOrder(Order order)
    {
        Orders.Add(order);
        SaveChanges();
    }

    public void SaveChanges() { }
}

public class Order
{
}
