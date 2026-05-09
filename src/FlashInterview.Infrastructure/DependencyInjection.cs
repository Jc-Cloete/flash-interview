using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure.SensitiveWords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashInterview.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlashInterviewInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");

        services.AddDbContext<FlashInterviewDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ISensitiveWordRepository, SqlSensitiveWordRepository>();
        services.AddScoped<SensitiveWordSeeder>();
        services.AddHostedService<DatabaseBootstrapper>();

        return services;
    }
}
