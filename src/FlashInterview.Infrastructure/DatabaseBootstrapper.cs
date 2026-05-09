using FlashInterview.Infrastructure.SensitiveWords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashInterview.Infrastructure;

public sealed class DatabaseBootstrapper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DatabaseBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var applyMigrations = IsEnabled("Database:ApplyMigrationsOnStartup");
        var legacyEnsureCreated = IsEnabled("Database:EnsureCreatedOnStartup");
        var seedOnStartup = IsEnabled("Database:SeedOnStartup");

        if (!applyMigrations && legacyEnsureCreated)
        {
            logger.LogWarning(
                "Database:EnsureCreatedOnStartup is deprecated; applying migrations instead. Use Database:ApplyMigrationsOnStartup.");
            applyMigrations = true;
        }

        if (!applyMigrations && !seedOnStartup)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlashInterviewDbContext>();

        if (applyMigrations)
        {
            logger.LogInformation("Applying database migrations");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        if (!seedOnStartup)
        {
            return;
        }

        var seedFile = configuration["Database:SeedFile"];
        if (string.IsNullOrWhiteSpace(seedFile))
        {
            logger.LogWarning("Database seeding is enabled but Database:SeedFile is not configured");
            return;
        }

        var seeder = scope.ServiceProvider.GetRequiredService<SensitiveWordSeeder>();
        var seededCount = await seeder.SeedFromFileAsync(seedFile, cancellationToken);
        logger.LogInformation("Sensitive word seed completed with {SeededCount} new rows", seededCount);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private bool IsEnabled(string key)
    {
        return bool.TryParse(configuration[key], out var enabled) && enabled;
    }
}
