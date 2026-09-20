namespace WindowsMcpNet.Security;

public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IpAllowlist _allowlist;

    public IpAllowlistMiddleware(RequestDelegate next, IpAllowlist allowlist)
    {
        _next = next;
        _allowlist = allowlist;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_allowlist.Allows(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Forbidden.");
            return;
        }

        await _next(context);
    }
}
