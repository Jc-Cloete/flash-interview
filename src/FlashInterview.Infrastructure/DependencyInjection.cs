using FlashInterview.Application.Auth;
using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure.Auth;
using FlashInterview.Infrastructure.SensitiveWords;
using Microsoft.AspNetCore.Identity;
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
        services.AddDataProtection();
        services
            .AddIdentityCore<FlashInterviewUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<FlashInterviewDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager();

        services.Configure<InitialSuperAdminOptions>(
            configuration.GetSection(InitialSuperAdminOptions.SectionName));
        services.AddScoped<IAuthWorkflow, AuthWorkflow>();
        services.AddScoped<IUserManagementWorkflow, UserManagementWorkflow>();
        services.AddScoped<ISensitiveWordRepository, SqlSensitiveWordRepository>();
        services.AddScoped<SensitiveWordSeeder>();
        services.AddHostedService<DatabaseBootstrapper>();
        services.AddHostedService<InitialSuperAdminBootstrapper>();

        return services;
    }
}
