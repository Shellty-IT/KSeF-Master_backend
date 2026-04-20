// Infrastructure/Extensions/EndpointExtensions.cs
namespace KSeF.Backend.Infrastructure.Extensions;

public static class EndpointExtensions
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        var defaultEnv = app.Configuration.GetValue<string>("KSeF:DefaultEnvironment") ?? "Test";

        app.MapGet("/", () => Results.Ok(new
        {
            status = "healthy",
            service = "KSeF Backend API",
            timestamp = DateTime.UtcNow,
            environment = $"KSeF {defaultEnv}"
        }));

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow
        }));

        return app;
    }
}