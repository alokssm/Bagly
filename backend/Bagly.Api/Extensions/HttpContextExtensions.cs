using System.Security.Claims;

namespace Bagly.Api.Extensions;

public static class HttpContextExtensions
{
    public static string? GetActorEmail(this HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.Email)
        ?? context.User.FindFirstValue("email")
        ?? context.User.Identity?.Name;

    public static string? GetClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();

    public static string GetRequestPath(this HttpContext context) =>
        $"{context.Request.Method} {context.Request.Path}";
}
