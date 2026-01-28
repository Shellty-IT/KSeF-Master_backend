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

var ksefBaseUrl = builder.Configuration.GetValue<string>("KSeF:BaseUrl") 
    ?? "https://ksef-test.mf.gov.pl/api/v2/";
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

builder.Services.AddSingleton<KSeFSessionManager>();
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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "KSeF Backend API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
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