using FlashInterview.Web.Clients;
using Microsoft.AspNetCore.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);
var webServiceName = builder.Configuration.GetValue("OpenTelemetry:ServiceName", "FlashInterview.Web");
var webOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "FlashInterview.Web")
        .WriteTo.Console();

    if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
    {
        loggerConfiguration.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = webOtlpEndpoint;
            options.Protocol = OtlpProtocol.Grpc;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = webServiceName,
                ["service.namespace"] = "FlashInterview"
            };
            options.IncludedData =
                IncludedData.MessageTemplateTextAttribute |
                IncludedData.MessageTemplateRenderingsAttribute |
                IncludedData.SourceContextAttribute |
                IncludedData.TraceIdField |
                IncludedData.SpanIdField |
                IncludedData.TemplateBody;
        });
    }
});

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(webServiceName));

    if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
    {
        logging.AddOtlpExporter(options => options.Endpoint = new Uri(webOtlpEndpoint));
    }
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(webServiceName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http");

        if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(webOtlpEndpoint));
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/healthz");
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(webOtlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(webOtlpEndpoint));
        }
    });

builder.Services.AddControllersWithViews();
builder.Services.Configure<SensitiveWordsApiOptions>(
    builder.Configuration.GetSection(SensitiveWordsApiOptions.SectionName));
builder.Services.AddHttpClient<SensitiveWordsApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SensitiveWordsApiOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (httpContext, next) =>
{
    var correlationId = GetOrCreateRequestHeader(httpContext, "X-Correlation-Id", httpContext.TraceIdentifier);
    var sessionId = GetOrCreateRequestHeader(httpContext, "X-Session-Id", Guid.NewGuid().ToString("n"));

    httpContext.Items["CorrelationId"] = correlationId;
    httpContext.Items["SessionId"] = sessionId;

    httpContext.Response.OnStarting(() =>
    {
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        httpContext.Response.Headers["X-Session-Id"] = sessionId;
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

    var startedAt = TimeProvider.System.GetTimestamp();

    try
    {
        await next(httpContext);
    }
    finally
    {
        var elapsed = TimeProvider.System.GetElapsedTime(startedAt);
        app.Logger.LogInformation(
            "HTTP request completed {RequestMethod} {RequestPath} {StatusCode} in {ElapsedMilliseconds} ms",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            httpContext.Response.StatusCode,
            elapsed.TotalMilliseconds);
    }
});

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async httpContext =>
    {
        var exceptionFeature = httpContext.Features.Get<IExceptionHandlerPathFeature>();
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

        if (exceptionFeature?.Error is not null)
        {
            logger.LogError(
                exceptionFeature.Error,
                "Unhandled exception while processing {RequestMethod} {RequestPath}",
                httpContext.Request.Method,
                exceptionFeature.Path);
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "text/html; charset=utf-8";
        await httpContext.Response.WriteAsync(
            "<!doctype html><html lang=\"en\"><head><title>Error</title></head><body><h1>Unexpected error</h1><p>The request could not be completed.</p></body></html>");
    });
});

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms ({Application})";
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
    {
        diagnosticContext.Set("Application", "FlashInterview.Web");
        if (_.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            diagnosticContext.Set("CorrelationId", correlationId);
        }

        if (_.Items.TryGetValue("SessionId", out var sessionId))
        {
            diagnosticContext.Set("SessionId", sessionId);
        }
    };
});
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Chat}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static string GetOrCreateRequestHeader(HttpContext httpContext, string headerName, string fallbackValue)
{
    var value = httpContext.Request.Headers[headerName].FirstOrDefault();
    return string.IsNullOrWhiteSpace(value) ? fallbackValue : value;
}
