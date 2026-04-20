// Program.cs
using KSeF.Backend.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppConfiguration();
builder.AddDatabase();
builder.AddAppAuthentication();
builder.AddHttpClients();
builder.AddAppServices();
builder.AddSwagger();
builder.AddAppCors();

builder.Services.AddControllers();

var app = builder.Build();

app.InitializeDatabase();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthEndpoints();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var url = $"http://0.0.0.0:{port}";

app.Logger.LogInformation("KSeF Backend API starting on: {Url}", url);
app.Logger.LogInformation("Swagger: {Url}/swagger", url);

app.Run(url);