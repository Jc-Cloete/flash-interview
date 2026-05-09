using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlashInterview.Application.SensitiveWords;
using FlashInterview.Infrastructure;
using FlashInterview.Infrastructure.SensitiveWords;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlashInterview.Tests;

public sealed class MssqlApiIntegrationTests : IAsyncLifetime
{
    private const string AdminApiKey = "mssql-test-admin-key";

    private readonly MssqlDatabaseFixture _database = new();

    public Task InitializeAsync()
    {
        return _database.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    [MssqlFact]
    public async Task SensitiveWordsEndpoints_PersistCrudChangesInMssql()
    {
        using var factory = new MssqlApiFactory(_database.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/sensitive-words",
            new CreateSensitiveWordRequest("  Flash Select  ", "Interview"));

        await AssertStatusCodeAsync(HttpStatusCode.Created, createResponse);
        var created = await ReadJsonAsync(createResponse);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Flash Select", created.GetProperty("value").GetString());
        Assert.Equal("FLASH SELECT", created.GetProperty("normalizedValue").GetString());
        Assert.Equal("interview", created.GetProperty("category").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        using var listResponse = await client.GetAsync("/api/sensitive-words?q=flash&category=interview&isActive=true");

        listResponse.EnsureSuccessStatusCode();
        var list = await ReadJsonAsync(listResponse);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());

        using var getResponse = await client.GetAsync($"/api/sensitive-words/{id}");

        getResponse.EnsureSuccessStatusCode();
        var fetched = await ReadJsonAsync(getResponse);
        Assert.Equal("Flash Select", fetched.GetProperty("value").GetString());

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/sensitive-words/{id}",
            new UpdateSensitiveWordRequest("Flash Update", "Updated", false));

        updateResponse.EnsureSuccessStatusCode();
        var updated = await ReadJsonAsync(updateResponse);
        Assert.Equal(id, updated.GetProperty("id").GetGuid());
        Assert.Equal("Flash Update", updated.GetProperty("value").GetString());
        Assert.Equal("updated", updated.GetProperty("category").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());

        using var deleteResponse = await client.DeleteAsync($"/api/sensitive-words/{id}");

        await AssertStatusCodeAsync(HttpStatusCode.NoContent, deleteResponse);

        using var deletedGetResponse = await client.GetAsync($"/api/sensitive-words/{id}");

        await AssertStatusCodeAsync(HttpStatusCode.NotFound, deletedGetResponse);
    }

    [MssqlFact]
    public async Task SensitiveWordsCreateEndpoint_ReturnsValidationErrorForPersistedDuplicate()
    {
        using var factory = new MssqlApiFactory(_database.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/sensitive-words",
            new CreateSensitiveWordRequest("Flash Duplicate", "sql"));

        firstResponse.EnsureSuccessStatusCode();

        using var duplicateResponse = await client.PostAsJsonAsync(
            "/api/sensitive-words",
            new CreateSensitiveWordRequest(" flash duplicate ", "sql"));

        await AssertStatusCodeAsync(HttpStatusCode.BadRequest, duplicateResponse);
        var problem = await ReadJsonAsync(duplicateResponse);
        Assert.True(problem.GetProperty("errors").TryGetProperty("Value", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [MssqlFact]
    public async Task SensitiveWordSeeder_AppliesMigrationsAndImportsAllSuppliedEntries()
    {
        var seedFile = Path.Combine(TestPaths.RepositoryRoot, "docs", "sql_sensitive_list.txt");

        await using var dbContext = _database.CreateDbContext();
        var migrationNames = await dbContext.Database.GetAppliedMigrationsAsync();
        var seeder = new SensitiveWordSeeder(dbContext);

        var inserted = await seeder.SeedFromFileAsync(seedFile, CancellationToken.None);
        var secondInserted = await seeder.SeedFromFileAsync(seedFile, CancellationToken.None);
        var storedCount = await dbContext.SensitiveWords.CountAsync(CancellationToken.None);

        Assert.Contains(migrationNames, name => name.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Equal(228, inserted);
        Assert.Equal(0, secondInserted);
        Assert.Equal(228, storedCount);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertStatusCodeAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected HTTP {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    private sealed class MssqlApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["Database:SeedOnStartup"] = "false",
                    ["Security:AdminApiKey"] = AdminApiKey
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<FlashInterviewDbContext>>();
                services.AddDbContext<FlashInterviewDbContext>(options => options.UseSqlServer(connectionString));
            });
        }
    }
}
