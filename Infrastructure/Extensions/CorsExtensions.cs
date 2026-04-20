// Infrastructure/Extensions/CorsExtensions.cs
namespace KSeF.Backend.Infrastructure.Extensions;

public static class CorsExtensions
{
    public static WebApplicationBuilder AddAppCors(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return builder;
    }
}