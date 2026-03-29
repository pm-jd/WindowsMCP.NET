using System.Security.Cryptography;

namespace WindowsMcpNet.Security;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, string apiKey)
    {
        _next = next;
        _apiKey = apiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing or invalid Authorization header. Expected: Bearer <api-key>");
            return;
        }

        var providedKey = authHeader["Bearer ".Length..].Trim();
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedKey),
            System.Text.Encoding.UTF8.GetBytes(_apiKey)))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await _next(context);
    }
}
