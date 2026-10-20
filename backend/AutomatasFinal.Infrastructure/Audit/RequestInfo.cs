using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AutomatasFinal.Infrastructure.Audit;

/// <summary>Datos de la petición actual (IP, navegador, usuario) para auditoría y sesiones.</summary>
public static class RequestInfo
{
    private const int MaxUserAgentLength = 512;

    public static IPAddress? GetIp(HttpContext? context)
    {
        var ip = context?.Connection.RemoteIpAddress;

        return ip is not null && ip.IsIPv4MappedToIPv6
            ? ip.MapToIPv4()
            : ip;
    }

    public static string? GetUserAgent(HttpContext? context)
    {
        var userAgent = context?.Request.Headers.UserAgent.ToString();

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        return userAgent.Length > MaxUserAgentLength
            ? userAgent[..MaxUserAgentLength]
            : userAgent;
    }

    public static Guid? GetUserId(ClaimsPrincipal? user)
    {
        var claim = user?.FindFirst(ClaimTypes.NameIdentifier);

        return Guid.TryParse(claim?.Value, out var userId)
            ? userId
            : null;
    }
}
