using FlashInterview.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Tests;

public sealed class MssqlDatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"FlashInterviewTests_{Guid.NewGuid():N}";

    public string ConnectionString => MssqlTestDatabase.CreateDatabaseConnectionString(_databaseName);

    public async Task InitializeAsync()
    {
        if (!MssqlTestDatabase.IsAvailable())
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!MssqlTestDatabase.IsAvailable())
        {
            return;
        }

        await MssqlTestDatabase.DropDatabaseAsync(_databaseName);
    }

    public FlashInterviewDbContext CreateDbContext()
    {
        return MssqlTestDatabase.CreateDbContext(ConnectionString);
    }
}
