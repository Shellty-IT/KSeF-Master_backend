// Infrastructure/KSeF/KSeFErrorParser.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KSeF.Backend.Infrastructure.KSeF;

public static class KSeFErrorParser
{
    public static string ExtractError(string responseBody, string fallback)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) 
            return fallback;

        try
        {
            var node = JsonNode.Parse(responseBody);
            if (node == null) return fallback;

            var exceptionDetails = node["exception"]?["exceptionDetailList"];
            if (exceptionDetails is JsonArray arr && arr.Count > 0)
            {
                var first = arr[0];
                var desc = first?["exceptionDescription"]?.GetValue<string>();
                var details = first?["details"] as JsonArray;
                var detailStr = details?.Count > 0
                    ? " — " + string.Join("; ", details.Select(d => d?.GetValue<string>() ?? ""))
                    : "";
                if (!string.IsNullOrEmpty(desc))
                    return desc + detailStr;
            }

            var msg = node["message"]?.GetValue<string>()
                ?? node["error"]?.GetValue<string>()
                ?? node["description"]?.GetValue<string>();

            return !string.IsNullOrEmpty(msg) ? msg : fallback;
        }
        catch
        {
            return responseBody.Length > 300 ? responseBody[..300] : responseBody;
        }
    }

    public static string? ParseInvoiceError(string responseContent)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("exception", out var exception))
            {
                if (exception.TryGetProperty("exceptionDetailList", out var details)
                    && details.ValueKind == JsonValueKind.Array
                    && details.GetArrayLength() > 0
                    && details[0].TryGetProperty("exceptionDescription", out var desc))
                    return desc.GetString();

                if (exception.TryGetProperty("serviceCtx", out var ctx))
                    return ctx.GetString();
            }

            if (root.TryGetProperty("message", out var message)) return message.GetString();
            if (root.TryGetProperty("error", out var error)) return error.GetString();
        }
        catch { }

        return null;
    }
}