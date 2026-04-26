// Infrastructure/Extensions/HttpClientExtensions.cs
using KSeF.Backend.Infrastructure.KSeF;


namespace KSeF.Backend.Infrastructure.Extensions;

public static class HttpClientExtensions
{
    public static WebApplicationBuilder AddHttpClients(this WebApplicationBuilder builder)
    {
        var defaultEnv = builder.Configuration.GetValue<string>("KSeF:DefaultEnvironment") ?? "Test";
        var ksefBaseUrl = builder.Configuration.GetValue<string>($"KSeF:Environments:{defaultEnv}:ApiBaseUrl")
                          ?? "https://api-test.ksef.mf.gov.pl/v2/";
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

        return builder;
    }
}