using KSeF.Backend.Services;
using KSeF.Backend.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════
// SERVICES
// ═══════════════════════════════════════════════════════════════

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
});

// HttpClient dla KSeF
var ksefBaseUrl = builder.Configuration.GetValue<string>("KSeF:BaseUrl") 
    ?? "https://ksef-test.mf.gov.pl/api/v2/";
var timeoutSeconds = builder.Configuration.GetValue<int>("KSeF:TimeoutSeconds", 60);

builder.Services.AddHttpClient("KSeF", client =>
{
    client.BaseAddress = new Uri(ksefBaseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

// Session Manager - Singleton (przechowuje sesję w pamięci)
builder.Services.AddSingleton<KSeFSessionManager>();

// Services - Scoped
builder.Services.AddScoped<IKSeFCryptoService, KSeFCryptoService>();
builder.Services.AddScoped<IKSeFAuthService, KSeFAuthService>();
builder.Services.AddScoped<IKSeFInvoiceService, KSeFInvoiceService>();
builder.Services.AddScoped<InvoiceXmlGenerator>();
builder.Services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();

// CORS - dla frontendu na Netlify
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()    // Pozwala na requesty z dowolnego originu (Netlify)
            .AllowAnyMethod()    // GET, POST, PUT, DELETE, etc.
            .AllowAnyHeader();   // Content-Type, Authorization, etc.
    });
});

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════
// MIDDLEWARE
// ═══════════════════════════════════════════════════════════════

// Swagger UI - zawsze włączony (przydatne do testów)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "KSeF Backend API v1");
    c.RoutePrefix = "swagger";
});

// CORS musi być przed MapControllers
app.UseCors();

// Mapuj kontrolery
app.MapControllers();

// Health check endpoint (dla Render.com)
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

// ═══════════════════════════════════════════════════════════════
// START
// ═══════════════════════════════════════════════════════════════

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var url = $"http://0.0.0.0:{port}";

app.Logger.LogInformation("═══════════════════════════════════════════════════════════════");
app.Logger.LogInformation("  KSeF Backend API");
app.Logger.LogInformation("  Starting on: {Url}", url);
app.Logger.LogInformation("  Swagger UI: {Url}/swagger", url);
app.Logger.LogInformation("  KSeF API: {KSeFUrl}", ksefBaseUrl);
app.Logger.LogInformation("═══════════════════════════════════════════════════════════════");

app.Run(url);