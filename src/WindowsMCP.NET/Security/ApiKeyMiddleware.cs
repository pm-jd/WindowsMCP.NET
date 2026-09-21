using System.Security.Cryptography;
using System.Text;

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
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!HasBearerScheme(authHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing or invalid Authorization header. Expected: Bearer <api-key>");
            return;
        }

        if (!Matches(authHeader, _apiKey))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await _next(context);
    }

    /// <summary>True when the header is "Bearer &lt;key&gt;" and the key equals <paramref name="apiKey"/> (constant-time compare).</summary>
    public static bool Matches(string? authorizationHeader, string apiKey)
    {
        if (!HasBearerScheme(authorizationHeader))
            return false;

        var providedKey = authorizationHeader!["Bearer ".Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedKey),
            Encoding.UTF8.GetBytes(apiKey));
    }

    private static bool HasBearerScheme(string? authorizationHeader) =>
        !string.IsNullOrEmpty(authorizationHeader)
        && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}
