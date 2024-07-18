using Imazen.Routing.Matching;
using Xunit;

namespace Imazen.Tests.Routing.Matching;
public class CharacterClassTests
{
    [Theory]
    [InlineData("[0-9]", true, "0123456789")]
    [InlineData("[a-zA-Z]", true, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("[^/]", false, "/")]
    [InlineData(@"[\t\n\r]", true, "\t\n\r")]
    [InlineData(@"[\[\]\{\}\,]", true, "[]{},")]
    [InlineData("[0-9a-fA-F]", true, "0123456789abcdefABCDEF")]
    [InlineData("[\\w]", true, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_")]
    [InlineData("[a-z0-9_]", true, "abcdefghijklmnopqrstuvwxyz0123456789_")]
    [InlineData("[^a-z]", false, "abcdefghijklmnopqrstuvwxyz")]
    [InlineData("[^0-9]", false, "0123456789")]
    [InlineData("[a-zA-Z0-9]", true, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
    [InlineData("[^\\w]", false, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_")]
    [InlineData("[\\.]", true, ".")]
    [InlineData(@"[\+\-\*\/]", true, "+-*/")]
    [InlineData(@"[\(\)\[\]\{\}]", true, "()[]{}")]
    public void ValidCharacterClass_ShouldParseSuccessfully(string syntax, bool shouldMatch, string testChars)
    {
        // Arrange
        var success = CharacterClass.TryParse(syntax.AsMemory(), out var result, out var error);

        // Assert
        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(result);

        foreach (var c in testChars)
        {
            Assert.Equal(shouldMatch, result!.Contains(c));
        }
    }

    [Theory]
       [InlineData("[]")]
    [InlineData("[a-]")]
    [InlineData("[-a]")]
    [InlineData("[a--b]")]
    [InlineData("[z-a]")]
    [InlineData("[\\d]")]
    [InlineData("[\\s]")]
    [InlineData("[a\\]")]
    [InlineData("[a\\q]")]
    [InlineData("[a-z-A-Z]")]
    [InlineData("[0-9-a-z]")]
    [InlineData("[^]")]
    [InlineData("[^a-z-0-9]")]
    [InlineData("[a-z-^]")]
    [InlineData("[\\w-\\d]")]
    [InlineData("[a-z\\d-\\w]")]
    [InlineData("[a-z]|[a-z]")]
    public void InvalidCharacterClass_ShouldFailParsing(string syntax)
    {
        // Arrange
        var success = CharacterClass.TryParse(syntax.AsMemory(), out var result, out var error);

        // Assert
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Null(result);
    }
}