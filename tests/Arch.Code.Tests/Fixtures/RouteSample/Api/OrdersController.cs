namespace Api;

// Fixture source for RouteScanner. Deliberately varied: an attribute-routed action, a
// convention-routed one with no verb attribute, and an unresolved case (route built
// from a constant, not a literal).
[ApiController]
[Route("api/[controller]")]
public class OrdersController
{
    private const string DynamicSegment = "computed-at-runtime";

    [HttpGet("{id}")]
    public int GetById(int id) => id;

    // No [HttpPost] — convention-routed from the method name.
    public int Post(int order) => order;

    [HttpGet(DynamicSegment)]
    public int Unresolvable() => 0;
}
