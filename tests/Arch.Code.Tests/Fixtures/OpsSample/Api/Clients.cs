namespace Api;

// Fixture source for the ops/network scan. The URLs below are deliberately varied: encrypted and
// plaintext schemes, an explicit port and a default one, a loopback address, a deploy-time token,
// and a documentation link that must NOT be reported as an endpoint.
public static class Clients
{
    // A real external dependency, TLS, non-default port.
    public const string Payments = "https://payments.partner.example.net:8443/v2/charge";

    // Plaintext HTTP to an internal host — the finding an ops reviewer wants flagged.
    public const string Inventory = "http://inventory.internal.example.net/api/stock";

    // Message broker, plaintext scheme.
    public const string Broker = "amqp://rabbit.internal.example.net:5672";

    // Resolved at deploy time — a real dependency whose target this scan cannot know.
    public const string Notifications = "https://{notifyHost}/send";

    // Loopback left in committed source: works on a dev box, fails everywhere else.
    public const string LocalDev = "http://localhost:5005/debug";

    // See https://learn.microsoft.com/dotnet/api/system.net.http.httpclient — documentation,
    // not an endpoint, and must be filtered out.
    public static string Describe() => $"{Payments} {Inventory} {Broker} {Notifications} {LocalDev}";
}
