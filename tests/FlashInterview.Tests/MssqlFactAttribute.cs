namespace FlashInterview.Tests;

internal sealed class MssqlFactAttribute : FactAttribute
{
    public MssqlFactAttribute()
    {
        if (!MssqlTestDatabase.IsAvailable())
        {
            Skip = "MSSQL integration tests skipped because localhost SQL Server is not available. Set FLASHINTERVIEW_TEST_MSSQL_MASTER to override the connection string.";
        }
    }
}
