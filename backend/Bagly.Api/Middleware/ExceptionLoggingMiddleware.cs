using System.Net;
using System.Text.Json;
using Bagly.Api.Services;

namespace Bagly.Api.Middleware;

public class ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAuditLogService auditLog)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var actor = context.User?.Identity?.Name
                ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            try
            {
                await auditLog.LogAsync(
                    category: "Error",
                    action: "UnhandledException",
                    message: ex.Message,
                    level: "Error",
                    actorEmail: actor,
                    details: new
                    {
                        exceptionType = ex.GetType().FullName,
                        stackTrace = ex.StackTrace,
                        method = context.Request.Method,
                    },
                    ipAddress: context.Connection.RemoteIpAddress?.ToString(),
                    requestPath: $"{context.Request.Method} {context.Request.Path}");
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to write audit log for unhandled exception.");
            }

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "An unexpected error occurred.",
                }));
            }
        }
    }
}
