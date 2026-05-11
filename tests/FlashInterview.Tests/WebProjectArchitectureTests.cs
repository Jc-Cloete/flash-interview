using System.Xml.Linq;

namespace FlashInterview.Tests;

public sealed class WebProjectArchitectureTests
{
    [Fact]
    public void WebProject_ReferencesOnlyApplicationContractsFromSolutionProjects()
    {
        var projectPath = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web", "FlashInterview.Web.csproj");
        var document = XDocument.Load(projectPath);

        var projectReferences = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        var unexpectedProjectReferences = projectReferences
            .Where(reference =>
            {
                var normalizedReference = reference
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                return !normalizedReference.EndsWith(
                    Path.Combine("FlashInterview.Application", "FlashInterview.Application.csproj"),
                    StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.Empty(unexpectedProjectReferences);
    }

    [Fact]
    public void WebProject_DoesNotReferenceEntityFrameworkOrSqlClientPackages()
    {
        var projectPath = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web", "FlashInterview.Web.csproj");
        var document = XDocument.Load(projectPath);

        var packageReferences = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(packageReferences, package => package.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageReferences, package => package.Contains("SqlClient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WebProject_SourceDoesNotUseDatabaseInfrastructureNamespaces()
    {
        var webSourceRoot = Path.Combine(TestPaths.RepositoryRoot, "src", "FlashInterview.Web");
        var projectPath = Path.Combine(webSourceRoot, "FlashInterview.Web.csproj");
        var sourceFiles = EnumerateWebCompileSourceFiles(projectPath, webSourceRoot)
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

    private static IEnumerable<string> EnumerateWebCompileSourceFiles(string projectPath, string webSourceRoot)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not resolve project directory for {projectPath}.");
        var document = XDocument.Load(projectPath);

        var localSourceFiles = Directory.EnumerateFiles(webSourceRoot, "*.cs", SearchOption.AllDirectories);
        var linkedSourceFiles = document.Descendants("Compile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(include => ExpandCompileInclude(projectDirectory, include!));

        return localSourceFiles
            .Concat(linkedSourceFiles)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExpandCompileInclude(string projectDirectory, string include)
    {
        var fullPattern = Path.GetFullPath(
            include
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar),
            projectDirectory);

        if (!fullPattern.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(fullPattern) ? [fullPattern] : [];
        }

        var directory = Path.GetDirectoryName(fullPattern)
            ?? throw new InvalidOperationException($"Could not resolve compile include directory for {include}.");
        var searchPattern = Path.GetFileName(fullPattern);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            : [];
    }
}
