using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Tests;

public sealed class SensitiveWordSeedParserTests
{
    [Fact]
    public void Parse_ReturnsTrimmedEntriesFromProvidedListFormat()
    {
        const string source = """
        [
            "ACTION"
            ,"ADD"
            ,"SELECT * FROM"
        ]
        """;

        var entries = SensitiveWordSeedParser.Parse(source).ToArray();

        Assert.Equal(["ACTION", "ADD", "SELECT * FROM"], entries);
    }

    [Fact]
    public void Parse_IgnoresBlankLinesAndBrackets()
    {
        const string source = """
        [

            "DROP"

        ]
        """;

        var entries = SensitiveWordSeedParser.Parse(source).ToArray();

        Assert.Equal(["DROP"], entries);
    }

    [Fact]
    public async Task Parse_ReadsDocumentedSqlSensitiveList()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "docs", "sql_sensitive_list.txt"));

        var entries = SensitiveWordSeedParser.Parse(source).ToArray();

        Assert.Equal(228, entries.Length);
        Assert.Equal("ACTION", entries[0]);
        Assert.Contains("CURRENT_USER", entries);
        Assert.Contains("SELECT * FROM", entries);
        Assert.Equal("SELECT * FROM", entries[^1]);
    }

    [Fact]
    public void Parse_PreservesCommaContainingPhrasesInsideQuotes()
    {
        const string source = """
        [
            "CURRENT, USER"
            ,"SELECT * FROM"
        ]
        """;

        var entries = SensitiveWordSeedParser.Parse(source).ToArray();

        Assert.Equal(["CURRENT, USER", "SELECT * FROM"], entries);
    }
}
