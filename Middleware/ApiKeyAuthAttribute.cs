// Middleware/ApiKeyAuthAttribute.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KSeF.Backend.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = configuration.GetValue<string>("SmartQuote:ApiKey");

        if (string.IsNullOrEmpty(expectedKey))
        {
            context.Result = new JsonResult(new { success = false, message = "API key not configured on server" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-API-Key", out var providedKey) ||
            providedKey.ToString() != expectedKey)
        {
            context.Result = new JsonResult(new { success = false, message = "Invalid API key" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        await next();
    }
}