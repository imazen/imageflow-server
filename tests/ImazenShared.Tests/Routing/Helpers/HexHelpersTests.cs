namespace Imazen.Routing.Tests.Helpers;


using Imazen.Routing.Helpers;
using System;
using Xunit;


public class HexHelpersTests
{
    [Fact]
    public void TestToHexLowercase()
    {
        // Arrange
        byte[] buffer = new byte[] { 0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F };

        // Act
        string result = buffer.ToHexLowercase();

        // Assert
        Assert.Equal("1a2b3c4d5e6f", result);
    }

    [Fact]
    public void TestToHexLowercaseWith()
    {
        // Arrange
        ReadOnlySpan<byte> buffer = new byte[] { 0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F };
        ReadOnlySpan<char> prefix = "prefix".ToCharArray();
        ReadOnlySpan<char> suffix = "suffix".ToCharArray();

        // Act
        string result = buffer.ToHexLowercaseWith(prefix, suffix);

        // Assert
        Assert.Equal("prefix1a2b3c4d5e6fsuffix", result);
    }
}