using FlashInterview.Web.Clients;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "FlashInterview.Web")
        .WriteTo.Console();
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
