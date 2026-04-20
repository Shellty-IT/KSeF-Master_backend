// Infrastructure/Extensions/DatabaseExtensions.cs
using Microsoft.EntityFrameworkCore;
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Infrastructure.Extensions;

public static class DatabaseExtensions
{
    public static WebApplicationBuilder AddDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                               ?? "Data Source=ksef_master.db";

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        return builder;
    }

    public static WebApplication InitializeDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialized (SQLite)");
        return app;
    }
}