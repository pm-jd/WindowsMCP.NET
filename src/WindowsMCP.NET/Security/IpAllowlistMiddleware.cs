namespace WindowsMcpNet.Security;

public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedIps;

    public IpAllowlistMiddleware(RequestDelegate next, IEnumerable<string> allowedIps)
    {
        _next = next;
        _allowedIps = new HashSet<string>(allowedIps);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_allowedIps.Count == 0)
        {
            await _next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (remoteIp is null || !_allowedIps.Contains(remoteIp))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync($"IP {remoteIp} is not in the allowlist.");
            return;
        }

        await _next(context);
    }
}
