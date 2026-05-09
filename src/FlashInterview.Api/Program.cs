using FlashInterview.Api.Health;
using FlashInterview.Api.Security;
using FlashInterview.Api.SensitiveWords;
using FlashInterview.Api.Telemetry;
using FlashInterview.Api.OpenApi;
using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "FlashInterview.Api")
        .WriteTo.Console();
});

var apiServiceName = builder.Configuration.GetValue("OpenTelemetry:ServiceName", "FlashInterview.Api");
var apiOtlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(apiServiceName))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(MaskingMetrics.MeterName)
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http");

        if (!string.IsNullOrWhiteSpace(apiOtlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(apiOtlpEndpoint));
        }
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/healthz")
                    && !httpContext.Request.Path.StartsWithSegments("/readyz");
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.EnrichWithIDbCommand = (activity, command) =>
                    activity.SetTag("db.statement", command.CommandText);
            });

        if (!string.IsNullOrWhiteSpace(apiOtlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(apiOtlpEndpoint));
        }
    });

builder.Services.AddFlashInterviewInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ISensitiveWordMatcherCache, SensitiveWordMatcherCache>();
builder.Services.AddSingleton<MaskingMetrics>();
builder.Services.AddControllers();
builder.Services
    .AddAuthentication(AdminApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AdminApiKeyAuthenticationHandler>(
        AdminApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminApiKey, policy =>
    {
        policy.AddAuthenticationSchemes(AdminApiKeyAuthenticationHandler.SchemeName);
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddRateLimiter(options =>
{
    var permitLimit = Math.Max(1, builder.Configuration.GetValue("Security:MaskRateLimit:PermitLimit", 60));
    var windowSeconds = Math.Max(1, builder.Configuration.GetValue("Security:MaskRateLimit:WindowSeconds", 60));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("MaskMessage", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.AddSecurityDefinition(AdminApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Name = AdminApiKeyAuthenticationHandler.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "API key required for internal sensitive-word administration endpoints."
    });
    options.OperationFilter<AdminApiKeyOperationFilter>();
    options.OperationFilter<RequestExampleOperationFilter>();
    options.OperationFilter<RequestParameterDescriptionOperationFilter>();
    options.DocumentFilter<HealthChecksDocumentFilter>();
});
builder.Services
    .AddHealthChecks()
    .AddCheck<SqlServerReadinessHealthCheck>("mssql", tags: ["ready"]);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

        await Results.Problem(
            title: "Unexpected server error",
            statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(httpContext);
    });
});

app.UseForwardedHeaders();
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
        diagnosticContext.Set("Application", "FlashInterview.Api");
    };
});
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;
