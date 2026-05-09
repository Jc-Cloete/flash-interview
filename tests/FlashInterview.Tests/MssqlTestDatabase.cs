using FlashInterview.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Tests;

internal static class MssqlTestDatabase
{
    private const string DefaultMasterConnectionString =
        "Server=localhost,1433;Database=master;User Id=sa;Password=Your_strong_password123;TrustServerCertificate=True;Encrypt=True;Connect Timeout=2";

    public static string MasterConnectionString =>
        Environment.GetEnvironmentVariable("FLASHINTERVIEW_TEST_MSSQL_MASTER")
        ?? DefaultMasterConnectionString;

    public static bool IsAvailable()
    {
        try
        {
            using var connection = new SqlConnection(MasterConnectionString);
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string CreateDatabaseConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName,
            ConnectTimeout = 5
        };

        return builder.ConnectionString;
    }

    public static FlashInterviewDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FlashInterviewDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new FlashInterviewDbContext(options);
    }

    public static async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        var escapedName = EscapeDatabaseName(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName.Replace("'", "''", StringComparison.Ordinal)}') IS NOT NULL
            BEGIN
                ALTER DATABASE {escapedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {escapedName};
            END
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static string EscapeDatabaseName(string databaseName)
    {
        return $"[{databaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
