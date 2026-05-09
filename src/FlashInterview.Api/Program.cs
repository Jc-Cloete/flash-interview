using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "FlashInterview.Api")
        .WriteTo.Console();
});

builder.Services.AddFlashInterviewInfrastructure(builder.Configuration);
builder.Services.AddSingleton<SensitiveWordMasker>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
});
builder.Services.AddHealthChecks();

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
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapHealthChecks("/readyz");

app.Run();

public partial class Program;
