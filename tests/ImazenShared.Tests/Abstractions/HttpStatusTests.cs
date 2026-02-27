using Imazen.Abstractions.Resulting;
using Xunit;

namespace Imazen.Shared.Tests.Abstractions;

public class HttpStatusTests
{
    [Fact]
    public void Equals_MatchesOperatorEquals_SameCodeDifferentMessage()
    {
        var a = new HttpStatus(404, "Not Found");
        var b = new HttpStatus(404, "Item not found");

        // operator == and Equals() must agree
        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
    }

    [Fact]
    public void Equals_DifferentCodes_ReturnsFalse()
    {
        var a = new HttpStatus(200);
        var b = new HttpStatus(404);

        Assert.False(a == b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void WithMessage_PreservesEquality()
    {
        var original = HttpStatus.NotFound;
        var modified = original.WithMessage("Custom message");

        Assert.True(original == modified);
        Assert.True(original.Equals(modified));
        Assert.Equal(original.GetHashCode(), modified.GetHashCode());
    }

    [Fact]
    public void WithAppend_PreservesEquality()
    {
        var original = HttpStatus.ServerError;
        var modified = original.WithAppend("extra detail");

        Assert.True(original == modified);
        Assert.True(original.Equals(modified));
    }

    [Fact]
    public void HashSet_DeduplicatesByStatusCode()
    {
        var set = new HashSet<HttpStatus>
        {
            new HttpStatus(404, "Not Found"),
            new HttpStatus(404, "Missing"),
            new HttpStatus(200, "OK")
        };

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Dictionary_LookupByStatusCode()
    {
        var dict = new Dictionary<HttpStatus, string>
        {
            [new HttpStatus(404, "Not Found")] = "first"
        };

        // Lookup with different message should find the same entry
        Assert.True(dict.ContainsKey(new HttpStatus(404, "Missing")));
    }

    [Fact]
    public void CrossType_IntEquality()
    {
        var status = new HttpStatus(200);
        Assert.True(status == 200);
        Assert.True(200 == status);
        Assert.False(status == 404);
    }

    [Fact]
    public void CrossType_HttpStatusCodeEquality()
    {
        var status = new HttpStatus(200);
        Assert.True(System.Net.HttpStatusCode.OK == status);
        Assert.False(System.Net.HttpStatusCode.NotFound == status);
    }

    [Fact]
    public void NullMessage_Equality()
    {
        var a = new HttpStatus(200, null);
        var b = new HttpStatus(200, "OK");

        Assert.True(a == b);
        Assert.True(a.Equals(b));
    }
}
