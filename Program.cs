// Program.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using KSeF.Backend.Models.Data;
using KSeF.Backend.Services;
using KSeF.Backend.Services.Interfaces;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.GetCurrentDirectory()
});

builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=ksef_master.db";
    options.UseSqlite(connectionString);
});

var jwtKey = builder.Configuration.GetValue<string>("Jwt:Key") ?? "FallbackKeyForDev2025!MinLen32Chars!!";
var jwtIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer") ?? "KSeFMaster";
var jwtAudience = builder.Configuration.GetValue<string>("Jwt:Audience") ?? "KSeFMasterApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "KSeF Backend API",
        Version = "v1",
        Description = "Backend do integracji z Krajowym Systemem e-Faktur (środowisko testowe)"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Wprowadź token JWT: Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var ksefBaseUrl = builder.Configuration.GetValue<string>("KSeF:BaseUrl")
    ?? "https://api-test.ksef.mf.gov.pl/v2";
var timeoutSeconds = builder.Configuration.GetValue<int>("KSeF:TimeoutSeconds", 60);

builder.Services.AddTransient<KSeFHttpLoggingHandler>();

builder.Services.AddHttpClient("KSeF", client =>
{
    client.BaseAddress = new Uri(ksefBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

    client.DefaultRequestHeaders.Clear();
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "KSeF-Backend/1.0 (.NET)");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
})
.AddHttpMessageHandler<KSeFHttpLoggingHandler>();

builder.Services.AddHttpClient("SmartQuoteWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddSingleton<KSeFSessionManager>();
builder.Services.AddSingleton<IExternalDraftService, ExternalDraftService>();
builder.Services.AddSingleton<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IKSeFCryptoService, KSeFCryptoService>();
builder.Services.AddScoped<IKSeFAuthService, KSeFAuthService>();
builder.Services.AddScoped<IKSeFInvoiceService, KSeFInvoiceService>();
builder.Services.AddScoped<InvoiceXmlGenerator>();
builder.Services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    app.Logger.LogInformation("Database initialized (SQLite)");
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "KSeF Backend API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "KSeF Backend API",
    timestamp = DateTime.UtcNow,
    environment = "KSeF Test"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow
}));

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var url = $"http://0.0.0.0:{port}";

app.Logger.LogInformation("KSeF Backend API starting on: {Url}", url);
app.Logger.LogInformation("Swagger UI: {Url}/swagger", url);
app.Logger.LogInformation("KSeF API: {KSeFUrl}", ksefBaseUrl);

app.Run(url);

