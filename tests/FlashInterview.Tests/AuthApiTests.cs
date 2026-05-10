using FlashInterview.Application.Auth;
using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure;
using FlashInterview.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;

namespace FlashInterview.Tests;

public sealed class AuthApiTests
{
    private const string AdminApiKey = "auth-api-test-key";

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task LoginEndpoint_RejectsMissingOrInvalidAdminApiKey(string? suppliedApiKey)
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin@example.test", "Wrong_password123!"))
        };
        if (suppliedApiKey is not null)
        {
            request.Headers.Add("X-Admin-Api-Key", suppliedApiKey);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_RejectsBadPassword()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        await factory.SeedUserAsync(
            "admin@example.test",
            "Correct_password123!",
            "Admin User",
            ApplicationRoles.Admin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostLoginAsync(client, "admin@example.test", "Wrong_password123!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_ReturnsAuthenticatedUserForValidCredentials()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        var userId = await factory.SeedUserAsync(
            "admin@example.test",
            "Correct_password123!",
            "Admin User",
            ApplicationRoles.Admin,
            ApplicationRoles.SuperAdmin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostLoginAsync(client, "admin@example.test", "Correct_password123!");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
        Assert.NotNull(user);
        Assert.Equal(userId, user.Id);
        Assert.Equal("admin@example.test", user.Email);
        Assert.Equal("Admin User", user.DisplayName);
        Assert.Contains(ApplicationRoles.Admin, user.Roles);
        Assert.Contains(ApplicationRoles.SuperAdmin, user.Roles);
    }

    [Fact]
    public async Task ExternalSignInEndpoint_ReturnsAuthenticatedUserForExistingExternalLogin()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        var userId = await factory.SeedUserAsync(
            "admin@example.test",
            "Correct_password123!",
            "Admin User",
            ApplicationRoles.Admin);
        await factory.AddExternalLoginAsync(userId, "Google", "google-user-1");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "admin@example.test",
                EmailVerified: true,
                "Admin User"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
        Assert.NotNull(user);
        Assert.Equal(userId, user.Id);
        Assert.Equal("admin@example.test", user.Email);
        Assert.Contains(ApplicationRoles.Admin, user.Roles);
    }

    [Fact]
    public async Task ExternalSignInEndpoint_LinksExistingLocalUserByVerifiedEmailWithoutPassword()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        var userId = await factory.SeedUserAsync(
            "admin@example.test",
            "Correct_password123!",
            "Admin User",
            ApplicationRoles.Admin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "admin@example.test",
                EmailVerified: true,
                "Admin User"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
        Assert.NotNull(user);
        Assert.Equal(userId, user.Id);

        using var resolveResponse = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "admin@example.test",
                EmailVerified: true,
                "Admin User"));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
    }

    [Fact]
    public async Task ExternalSignInEndpoint_CreatesPlainUserForVerifiedGoogleEmail()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "new-user@example.test",
                EmailVerified: true,
                "New User"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
        Assert.NotNull(user);
        Assert.Equal("new-user@example.test", user.Email);
        Assert.Equal("New User", user.DisplayName);
        Assert.Empty(user.Roles);
        Assert.False(await factory.UserHasPasswordAsync("new-user@example.test"));
        Assert.True(await factory.UserHasExternalLoginAsync("new-user@example.test", "Google", "google-user-1"));
    }

    [Fact]
    public async Task ExternalSignInEndpoint_SupportsLegacyRoute()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "legacy-user@example.test",
                EmailVerified: true,
                "Legacy User"),
            "/api/auth/external/sign-in");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExternalSignInEndpoint_RejectsUnverifiedExternalEmail()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await PostExternalSignInAsync(
            client,
            new ExternalLoginRequest(
                "Google",
                "google-user-1",
                "admin@example.test",
                EmailVerified: false,
                "Admin User"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task UsersEndpoint_RejectsMissingOrInvalidAdminApiKey(string? suppliedApiKey)
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        if (suppliedApiKey is not null)
        {
            request.Headers.Add("X-Admin-Api-Key", suppliedApiKey);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsersEndpoint_ListsUsersWithEmailLockoutAndRolesInEmailOrder()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        await factory.SeedUserAsync(
            "bravo@example.test",
            "Correct_password123!",
            "Bravo User",
            ApplicationRoles.Admin);
        await factory.SeedUserAsync(
            "alpha@example.test",
            "Correct_password123!",
            "Alpha User",
            ApplicationRoles.SuperAdmin);
        await factory.SetLockoutAsync("alpha@example.test", DateTimeOffset.UtcNow.AddDays(1));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await GetUsersAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<UserListItemDto[]>();
        Assert.NotNull(users);
        Assert.Collection(
            users,
            user =>
            {
                Assert.Equal("alpha@example.test", user.Email);
                Assert.True(user.IsLockedOut);
                Assert.Contains(ApplicationRoles.SuperAdmin, user.Roles);
            },
            user =>
            {
                Assert.Equal("bravo@example.test", user.Email);
                Assert.False(user.IsLockedOut);
                Assert.Contains(ApplicationRoles.Admin, user.Roles);
            });
    }

    [Fact]
    public async Task UsersEndpoint_CreatesLocalUserWithOptionalAdminRole()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await CreateUserAsync(
            client,
            new CreateUserRequest(
                "created@example.test",
                "Created User",
                "Created_password123!",
                IsAdmin: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/users/", response.Headers.Location.OriginalString, StringComparison.Ordinal);
        var user = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(user);
        Assert.Equal("created@example.test", user.Email);
        Assert.Equal("Created User", user.DisplayName);
        Assert.Contains(ApplicationRoles.Admin, user.Roles);
        Assert.True(await factory.UserCanPasswordSignInAsync("created@example.test", "Created_password123!"));
    }

    [Fact]
    public async Task UsersEndpoint_RejectsDuplicateEmail()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        await factory.SeedUserAsync(
            "created@example.test",
            "Correct_password123!",
            "Created User");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await CreateUserAsync(
            client,
            new CreateUserRequest(
                "created@example.test",
                "Duplicate User",
                "Created_password123!",
                IsAdmin: false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UsersEndpoint_GrantsAndRevokesAdminRoleAndUpdatesSecurityStamp()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        var userId = await factory.SeedUserAsync(
            "user@example.test",
            "Correct_password123!",
            "Managed User");
        var initialSecurityStamp = await factory.GetSecurityStampAsync("user@example.test");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var grantResponse = await UpdateAdminRoleAsync(client, userId, isAdmin: true);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);
        var grantedUser = await grantResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        var grantSecurityStamp = await factory.GetSecurityStampAsync("user@example.test");
        using var revokeResponse = await UpdateAdminRoleAsync(client, userId, isAdmin: false);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revokedUser = await revokeResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        var revokeSecurityStamp = await factory.GetSecurityStampAsync("user@example.test");

        Assert.NotNull(grantedUser);
        Assert.Contains(ApplicationRoles.Admin, grantedUser.Roles);
        Assert.NotEqual(initialSecurityStamp, grantSecurityStamp);
        Assert.NotNull(revokedUser);
        Assert.DoesNotContain(ApplicationRoles.Admin, revokedUser.Roles);
        Assert.NotEqual(grantSecurityStamp, revokeSecurityStamp);
    }

    [Fact]
    public async Task UsersEndpoint_DoesNotExposeSuperAdminRevocation()
    {
        await using var factory = new AuthApiFactory(AdminApiKey);
        var userId = await factory.SeedUserAsync(
            "owner@example.test",
            "Correct_password123!",
            "Owner User",
            ApplicationRoles.SuperAdmin);
        await factory.SeedUserAsync(
            "backup-owner@example.test",
            "Correct_password123!",
            "Backup Owner",
            ApplicationRoles.SuperAdmin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await DeleteSuperAdminRoleAsync(client, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await factory.UserIsInRoleAsync("owner@example.test", ApplicationRoles.SuperAdmin));
    }

    [Fact]
    public async Task InitialSuperAdminBootstrapper_CreatesConfiguredSuperAdminAndRoles()
    {
        await using var services = CreateIdentityServices(
            new Dictionary<string, string?>
            {
                ["Security:InitialSuperAdmin:Enabled"] = "true",
                ["Security:InitialSuperAdmin:Email"] = "owner@example.test",
                ["Security:InitialSuperAdmin:Password"] = "Owner_password123!"
            });

        await RunBootstrapperAsync(services);

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = await userManager.FindByEmailAsync("owner@example.test");

        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.True(await roleManager.RoleExistsAsync(ApplicationRoles.Admin));
        Assert.True(await roleManager.RoleExistsAsync(ApplicationRoles.SuperAdmin));
        Assert.True(await userManager.IsInRoleAsync(user, ApplicationRoles.Admin));
        Assert.True(await userManager.IsInRoleAsync(user, ApplicationRoles.SuperAdmin));
    }

    [Fact]
    public async Task InitialSuperAdminBootstrapper_IsIdempotent()
    {
        await using var services = CreateIdentityServices(
            new Dictionary<string, string?>
            {
                ["Security:InitialSuperAdmin:Enabled"] = "true",
                ["Security:InitialSuperAdmin:Email"] = "owner@example.test",
                ["Security:InitialSuperAdmin:Password"] = "Owner_password123!"
            });

        await RunBootstrapperAsync(services);
        await RunBootstrapperAsync(services);

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlashInterviewDbContext>();
        var user = await dbContext.Users.SingleAsync(user => user.Email == "owner@example.test");
        var roles = await dbContext.Roles
            .Where(role => role.Name == ApplicationRoles.Admin || role.Name == ApplicationRoles.SuperAdmin)
            .ToArrayAsync();
        var roleIds = roles.Select(role => role.Id).ToArray();
        var assignments = await dbContext.UserRoles
            .Where(userRole => userRole.UserId == user.Id && roleIds.Contains(userRole.RoleId))
            .ToArrayAsync();

        Assert.Equal(2, roles.Length);
        Assert.Equal(2, assignments.Length);
    }

    [Fact]
    public async Task InitialSuperAdminBootstrapper_ResetsExistingBootstrapUserPasswordToConfiguredPassword()
    {
        await using var services = CreateIdentityServices(
            new Dictionary<string, string?>
            {
                ["Security:InitialSuperAdmin:Enabled"] = "true",
                ["Security:InitialSuperAdmin:Email"] = "owner@example.test",
                ["Security:InitialSuperAdmin:Password"] = "New_owner_password123!"
            });

        using (var scope = services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = new FlashInterviewUser
            {
                UserName = "owner@example.test",
                Email = "owner@example.test",
                EmailConfirmed = true,
                DisplayName = "Existing Owner"
            };
            var createResult = await userManager.CreateAsync(user, "Old_owner_password123!");
            Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        await RunBootstrapperAsync(services);

        using (var scope = services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync("owner@example.test");

            Assert.NotNull(user);
            Assert.False(await userManager.CheckPasswordAsync(user, "Old_owner_password123!"));
            Assert.True(await userManager.CheckPasswordAsync(user, "New_owner_password123!"));
        }
    }

    private static ServiceProvider CreateIdentityServices(IReadOnlyDictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<InitialSuperAdminOptions>(configuration.GetSection(InitialSuperAdminOptions.SectionName));
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        services.AddDbContext<FlashInterviewDbContext>((serviceProvider, options) =>
            options.UseSqlite(serviceProvider.GetRequiredService<SqliteConnection>()));
        services
            .AddIdentityCore<FlashInterviewUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<FlashInterviewDbContext>()
            .AddDefaultTokenProviders()
            .AddSignInManager();

        var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlashInterviewDbContext>();
        dbContext.Database.EnsureCreated();
        return serviceProvider;
    }

    private static async Task RunBootstrapperAsync(ServiceProvider services)
    {
        using var scope = services.CreateScope();
        var bootstrapper = new InitialSuperAdminBootstrapper(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<IOptions<InitialSuperAdminOptions>>(),
            scope.ServiceProvider.GetRequiredService<ILogger<InitialSuperAdminBootstrapper>>());

        await bootstrapper.StartAsync(CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostExternalSignInAsync(
        HttpClient client,
        ExternalLoginRequest externalLogin,
        string path = "/api/auth/external-login/sign-in")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(externalLogin)
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetUsersAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CreateUserAsync(
        HttpClient client,
        CreateUserRequest createUser)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(createUser)
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateAdminRoleAsync(
        HttpClient client,
        string userId,
        bool isAdmin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{userId}/roles/admin")
        {
            Content = JsonContent.Create(new UserRoleUpdateRequest(isAdmin))
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteSuperAdminRoleAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{userId}/roles/super-admin");
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        return await client.SendAsync(request);
    }

    private sealed class AuthApiFactory(string adminApiKey) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection sqliteConnection = new("DataSource=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=FlashInterviewAuthTests;Trusted_Connection=True;",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["Database:SeedOnStartup"] = "false",
                    ["Security:InitialSuperAdmin:Enabled"] = "false",
                    ["Security:AdminApiKey"] = adminApiKey
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<FlashInterviewDbContext>>();
                services.RemoveAll<DbContextOptions<FlashInterviewDbContext>>();
                services.RemoveAll<ISensitiveWordRepository>();
                sqliteConnection.Open();
                services.AddDbContext<FlashInterviewDbContext>(options =>
                    options.UseSqlite(sqliteConnection));
                services.AddSingleton<ISensitiveWordRepository, EmptySensitiveWordRepository>();
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FlashInterviewDbContext>();
            dbContext.Database.EnsureCreated();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                sqliteConnection.Dispose();
            }
        }

        public async Task<string> SeedUserAsync(
            string email,
            string password,
            string? displayName,
            params string[] roles)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                    Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
                }
            }

            var user = new FlashInterviewUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName
            };
            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));

            foreach (var role in roles)
            {
                var roleResult = await userManager.AddToRoleAsync(user, role);
                Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }

            return user.Id;
        }

        public async Task AddExternalLoginAsync(string userId, string provider, string providerKey)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.NotNull(user);

            var loginResult = await userManager.AddLoginAsync(
                user,
                new UserLoginInfo(provider, providerKey, provider));
            Assert.True(loginResult.Succeeded, string.Join("; ", loginResult.Errors.Select(error => error.Description)));
        }

        public async Task SetLockoutAsync(string email, DateTimeOffset lockoutEnd)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var enabledResult = await userManager.SetLockoutEnabledAsync(user, true);
            Assert.True(enabledResult.Succeeded, string.Join("; ", enabledResult.Errors.Select(error => error.Description)));
            var endResult = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
            Assert.True(endResult.Succeeded, string.Join("; ", endResult.Errors.Select(error => error.Description)));
        }

        public async Task<bool> UserIsInRoleAsync(string email, string role)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            return await userManager.IsInRoleAsync(user, role);
        }

        public async Task<bool> UserCanPasswordSignInAsync(string email, string password)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            return await userManager.CheckPasswordAsync(user, password);
        }

        public async Task<bool> UserHasPasswordAsync(string email)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            return await userManager.HasPasswordAsync(user);
        }

        public async Task<bool> UserHasExternalLoginAsync(string email, string provider, string providerKey)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            var logins = await userManager.GetLoginsAsync(user);
            return logins.Any(login =>
                string.Equals(login.LoginProvider, provider, StringComparison.Ordinal) &&
                string.Equals(login.ProviderKey, providerKey, StringComparison.Ordinal));
        }

        public async Task<string?> GetSecurityStampAsync(string email)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            return await userManager.GetSecurityStampAsync(user);
        }

        public async Task<bool> UserIsLockedOutAsync(string email)
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<FlashInterviewUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);

            return await userManager.IsLockedOutAsync(user);
        }
    }

    private sealed class EmptySensitiveWordRepository : ISensitiveWordRepository
    {
        public Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PagedResponse<SensitiveWordDto>([], query.Page, query.PageSize, 0));
        }

        public Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<SensitiveWordDto?>(null);
        }

        public Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SensitiveWordCandidate>>([]);
        }
    }
}
