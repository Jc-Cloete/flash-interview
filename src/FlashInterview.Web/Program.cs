using FlashInterview.Application.Auth;
using FlashInterview.Web.Auth;
using FlashInterview.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Events;

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
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.Configure<SensitiveWordsApiOptions>(
    builder.Configuration.GetSection(SensitiveWordsApiOptions.SectionName));
builder.Services.Configure<AuthApiOptions>(
    builder.Configuration.GetSection(AuthApiOptions.SectionName));
builder.Services.PostConfigure<AuthApiOptions>(options =>
{
    options.BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
        ? builder.Configuration["SensitiveWordsApi:BaseUrl"]
        : options.BaseUrl;
    options.AdminApiKey = string.IsNullOrWhiteSpace(options.AdminApiKey)
        ? builder.Configuration["SensitiveWordsApi:AdminApiKey"]
        : options.AdminApiKey;
});
builder.Services.AddHttpClient<SensitiveWordsApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<SensitiveWordsApiOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient<AuthApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthApiOptions>>()
        .Value;

    if (string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        throw new InvalidOperationException("Auth API base URL is required.");
    }

    if (string.IsNullOrWhiteSpace(options.AdminApiKey))
    {
        throw new InvalidOperationException("Auth API admin API key is required.");
    }

    client.BaseAddress = new Uri(options.BaseUrl);
});
var cookieSecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
var authCookieName = builder.Environment.IsDevelopment()
    ? "FlashInterview.Web.Auth"
    : "__Host-FlashInterview.Web.Auth";
var externalCookieName = builder.Environment.IsDevelopment()
    ? "FlashInterview.Web.External"
    : "__Host-FlashInterview.Web.External";
var authenticationBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = authCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddCookie("FlashInterview.External", options =>
    {
        options.Cookie.Name = externalCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.SignInScheme = "FlashInterview.External";
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.ClaimActions.MapJsonKey("verified_email", "verified_email");
        options.Events.OnCreatingTicket = context =>
        {
            if (context.Identity is not null)
            {
                GoogleEmailVerificationClaims.AddVerifiedEmailClaim(context.Identity, context.User);
            }

            return Task.CompletedTask;
        };
        options.CorrelationCookie.SecurePolicy = cookieSecurePolicy;
        if (builder.Environment.IsDevelopment())
        {
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        }
    });
}
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAuthorizationPolicies.AdminOrSuperAdmin, policy =>
        policy.RequireRole(ApplicationRoles.Admin, ApplicationRoles.SuperAdmin));
    options.AddPolicy(AdminAuthorizationPolicies.SuperAdmin, policy =>
        policy.RequireRole(ApplicationRoles.SuperAdmin));
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

    await next(httpContext);
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

app.UseAuthentication();
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
    return IsValidLogDiscoveryIdentifier(value) ? value! : NormalizeLogDiscoveryIdentifier(fallbackValue);
}

static bool IsValidLogDiscoveryIdentifier(string? value)
{
    return !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && value.All(IsLogDiscoveryIdentifierCharacter);
}

static string NormalizeLogDiscoveryIdentifier(string value)
{
    var normalized = new string(value.Where(IsLogDiscoveryIdentifierCharacter).Take(64).ToArray());
    return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("n") : normalized;
}

static bool IsLogDiscoveryIdentifierCharacter(char value)
{
    return char.IsAsciiLetterOrDigit(value) || value is '-' or '_';
}

public partial class Program;
