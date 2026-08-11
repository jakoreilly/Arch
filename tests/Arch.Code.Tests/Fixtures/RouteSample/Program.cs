// Fixture source for RouteScanner's minimal-API recognition: top-level statements
// registering a route via app.MapGet.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => "OK");

app.Run();
