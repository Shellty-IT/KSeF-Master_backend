// Infrastructure/Extensions/ConfigurationExtensions.cs
namespace KSeF.Backend.Infrastructure.Extensions;

public static class ConfigurationExtensions
{
    public static WebApplicationBuilder AddAppConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        return builder;
    }
}