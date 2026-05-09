namespace FlashInterview.Tests;

public sealed class DatabaseBootstrapTests
{
    [Fact]
    public void InfrastructureProject_ContainsInitialCreateMigration()
    {
        var migrationsRoot = Path.Combine(
            TestPaths.RepositoryRoot,
            "src",
            "FlashInterview.Infrastructure",
            "Migrations");

        var migrationFiles = Directory.Exists(migrationsRoot)
            ? Directory.EnumerateFiles(migrationsRoot, "*InitialCreate.cs", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
                .ToArray()
            : [];

        Assert.Single(migrationFiles);
    }

    [Fact]
    public void DatabaseBootstrapper_UsesMigrationsAndSeparateSeedFlag()
    {
        var bootstrapperPath = Path.Combine(
            TestPaths.RepositoryRoot,
            "src",
            "FlashInterview.Infrastructure",
            "DatabaseBootstrapper.cs");

        var source = File.ReadAllText(bootstrapperPath);

        Assert.Contains("Database:ApplyMigrationsOnStartup", source, StringComparison.Ordinal);
        Assert.Contains("Database:SeedOnStartup", source, StringComparison.Ordinal);
        Assert.Contains("MigrateAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreatedAsync", source, StringComparison.Ordinal);
    }
}
