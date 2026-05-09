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
        if (!bool.TryParse(configuration["Database:EnsureCreatedOnStartup"], out var ensureCreated) || !ensureCreated)
        {
            return;
        }

        logger.LogInformation("Ensuring database exists");
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlashInterviewDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var seedFile = configuration["Database:SeedFile"];
        if (string.IsNullOrWhiteSpace(seedFile))
        {
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
}
