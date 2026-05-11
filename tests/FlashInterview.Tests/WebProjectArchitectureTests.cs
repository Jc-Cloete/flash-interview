using System.Text.Json;
using System.Xml.Linq;

namespace FlashInterview.Tests;

public sealed class WebProjectArchitectureTests
{
    [Fact]
    public void ApiComposition_DoesNotOwnIdentityWorkflowImplementations()
    {
        var apiSourceRoot = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Api");
        var sourceFiles = Directory
            .EnumerateFiles(apiSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var forbiddenUsages = sourceFiles
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(entry =>
                entry.line.Contains("namespace FlashInterview.Api.Auth", StringComparison.Ordinal)
                || entry.line.Contains("namespace FlashInterview.Api.Users", StringComparison.Ordinal)
                || entry.line.Contains("using FlashInterview.Api.Auth", StringComparison.Ordinal)
                || entry.line.Contains("using FlashInterview.Api.Users", StringComparison.Ordinal)
                || entry.line.Contains("using FlashInterview.Infrastructure.Auth", StringComparison.Ordinal)
                || entry.line.Contains("AddScoped<IAuthWorkflow, AuthWorkflow>", StringComparison.Ordinal)
                || entry.line.Contains("AddScoped<IUserManagementWorkflow, UserManagementWorkflow>", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, entry.path)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToArray();

        Assert.Empty(forbiddenUsages);
    }

    [Fact]
    public void WebProject_ReferencesOnlyAllowedSolutionProjectClosure()
    {
        var projectPath = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web", "FlashInterview.Web.csproj");
        var reachableProjects = GetReachableProjectReferences(projectPath);
        var allowedProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Application", "FlashInterview.Application.csproj")),
            Path.GetFullPath(Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Hosting", "FlashInterview.Hosting.csproj"))
        };

        var unexpectedProjectReferences = reachableProjects
            .Where(reference => !allowedProjectPaths.Contains(reference))
            .Select(reference => Path.GetRelativePath(TestPaths.RepositoryRoot, reference))
            .ToArray();

        Assert.Empty(unexpectedProjectReferences);
    }

    [Fact]
    public void WebProjectClosure_DoesNotReferenceEntityFrameworkOrSqlClientPackages()
    {
        var projectPath = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web", "FlashInterview.Web.csproj");
        var projectPaths = new[] { Path.GetFullPath(projectPath) }
            .Concat(GetReachableProjectReferences(projectPath));

        var packageReferences = projectPaths
            .SelectMany(path => XDocument
                .Load(path)
                .Descendants("PackageReference")
                .Select(element => new
                {
                    ProjectPath = path,
                    Package = element.Attribute("Include")?.Value
                }))
            .Where(reference => reference.Package is not null)
            .Select(reference => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, reference.ProjectPath)}: {reference.Package}")
            .ToArray();

        Assert.DoesNotContain(packageReferences, package => package.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageReferences, package => package.Contains("SqlClient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WebProjectResolvedPackageClosure_DoesNotContainDatabaseOrInfrastructurePackages()
    {
        var assetsPath = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), "project.assets.json not found; run 'dotnet restore' before running this test.");

        using var assetsFile = File.OpenRead(assetsPath);
        using var assetsDocument = JsonDocument.Parse(assetsFile);

        var resolvedPackageIds = assetsDocument.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var forbiddenResolvedPackages = resolvedPackageIds
            .Where(packageId =>
                packageId.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)
                || packageId.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)
                || packageId.StartsWith("FlashInterview.Infrastructure", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbiddenResolvedPackages);
    }

    [Fact]
    public void WebProject_SourceDoesNotUseDatabaseInfrastructureNamespaces()
    {
        var sourceRoots = new[]
        {
            Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web"),
            Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Hosting")
        };
        var sourceFiles = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var forbiddenUsages = sourceFiles
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(entry =>
                entry.line.Contains("FlashInterview.Infrastructure", StringComparison.Ordinal)
                || entry.line.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || entry.line.Contains("Microsoft.Data.SqlClient", StringComparison.Ordinal)
                || entry.line.Contains("System.Data.SqlClient", StringComparison.Ordinal)
                || entry.line.Contains("FlashInterviewDbContext", StringComparison.Ordinal)
                || entry.line.Contains("SqlSensitiveWordRepository", StringComparison.Ordinal)
                || entry.line.Contains("SensitiveWordEntity", StringComparison.Ordinal)
                || entry.line.Contains("SensitiveWordSeeder", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, entry.path)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToArray();

        Assert.Empty(forbiddenUsages);
    }

    private static IReadOnlyCollection<string> GetReachableProjectReferences(string rootProjectPath)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(GetProjectReferences(rootProjectPath));

        while (pending.Count > 0)
        {
            var projectPath = pending.Dequeue();
            if (!visited.Add(projectPath))
            {
                continue;
            }

            foreach (var referencePath in GetProjectReferences(projectPath))
            {
                pending.Enqueue(referencePath);
            }
        }

        return visited;
    }

    private static IEnumerable<string> GetProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not resolve project directory for {projectPath}.");

        return XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(
                value!
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar),
                projectDirectory));
    }
}
