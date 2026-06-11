using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace FlashInterview.Hosting;

public static class CorrelationExtensions
{
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string SessionHeaderName = "X-Session-Id";

    public static WebApplication UseFlashInterviewCorrelation(this WebApplication app)
    {
        app.Use(async (httpContext, next) =>
        {
            var correlationId = GetOrCreateRequestHeader(httpContext, CorrelationHeaderName, httpContext.TraceIdentifier);
            var sessionId = GetOrCreateRequestHeader(httpContext, SessionHeaderName, Guid.NewGuid().ToString("n"));

            httpContext.Items["CorrelationId"] = correlationId;
            httpContext.Items["SessionId"] = sessionId;

            httpContext.Response.OnStarting(() =>
            {
                httpContext.Response.Headers[CorrelationHeaderName] = correlationId;
                httpContext.Response.Headers[SessionHeaderName] = sessionId;
                return Task.CompletedTask;
            });

            using var loggerScope = app.Logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId,
                ["SessionId"] = sessionId,
                ["TraceIdentifier"] = httpContext.TraceIdentifier,
                ["RequestPath"] = httpContext.Request.Path.Value,
                ["RequestMethod"] = httpContext.Request.Method
            });
            using var correlationProperty = LogContext.PushProperty("CorrelationId", correlationId);
            using var sessionProperty = LogContext.PushProperty("SessionId", sessionId);
            using var traceIdentifierProperty = LogContext.PushProperty("TraceIdentifier", httpContext.TraceIdentifier);

            await next(httpContext);
        });

        return app;
    }

    private static string GetOrCreateRequestHeader(HttpContext httpContext, string headerName, string fallbackValue)
    {
        var value = httpContext.Request.Headers[headerName].FirstOrDefault();
        return IsValidLogDiscoveryIdentifier(value) ? value! : NormalizeLogDiscoveryIdentifier(fallbackValue);
    }

    private static bool IsValidLogDiscoveryIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 64
            && value.All(IsLogDiscoveryIdentifierCharacter);
    }

    private static string NormalizeLogDiscoveryIdentifier(string value)
    {
        var normalized = new string(value.Where(IsLogDiscoveryIdentifierCharacter).Take(64).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("n") : normalized;
    }

    private static bool IsLogDiscoveryIdentifierCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '-' or '_';
    }
}
