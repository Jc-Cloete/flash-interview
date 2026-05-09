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
}
