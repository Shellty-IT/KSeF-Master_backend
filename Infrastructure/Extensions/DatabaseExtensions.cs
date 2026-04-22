// Infrastructure/Extensions/DatabaseExtensions.cs
using Microsoft.EntityFrameworkCore;
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Infrastructure.Extensions;

public static class DatabaseExtensions
{
    public static WebApplicationBuilder AddDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
        });

        return builder;
    }

    public static WebApplication InitializeDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count > 0)
        {
            app.Logger.LogInformation("Applying {Count} pending migrations...", pendingMigrations.Count);
            db.Database.Migrate();
        }

        app.Logger.LogInformation("Database initialized (PostgreSQL/Neon)");
        return app;
    }
}