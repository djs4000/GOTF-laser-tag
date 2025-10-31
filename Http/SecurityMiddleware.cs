using System.Net;
using System.Security.Cryptography;
using System.Text;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Http;

/// <summary>
/// Middleware enforcing CIDR allowlist and optional bearer token authentication.
/// </summary>
public sealed class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityMiddleware> _logger;
    private readonly HttpOptions _options;

    public SecurityMiddleware(RequestDelegate next, IOptions<HttpOptions> options, ILogger<SecurityMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CidrAllowlistService allowlist)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            _logger.LogWarning("Request without remote IP rejected");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (remoteAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (!allowlist.IsAllowed(remoteAddress))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var header = authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var token = header[7..].Trim();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(token),
                    Encoding.UTF8.GetBytes(_options.BearerToken!)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }
}
