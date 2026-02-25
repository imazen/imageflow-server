using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class CacheKeyTests
{
    [Fact]
    public void FromStrings_ProducesDeterministicKeys()
    {
        var key1 = CacheKey.FromStrings("/images/photo.jpg", "width=400&format=webp");
        var key2 = CacheKey.FromStrings("/images/photo.jpg", "width=400&format=webp");

        Assert.Equal(key1, key2);
        Assert.Equal(key1.ToStringKey(), key2.ToStringKey());
        Assert.Equal(key1.ToStoragePath(), key2.ToStoragePath());
    }

    [Fact]
    public void DifferentVariants_ProduceDifferentKeys()
    {
        var key1 = CacheKey.FromStrings("/images/photo.jpg", "width=400");
        var key2 = CacheKey.FromStrings("/images/photo.jpg", "width=800");

        Assert.NotEqual(key1, key2);
        // Same source hash (same source path)
        Assert.Equal(key1.SourcePrefix(), key2.SourcePrefix());
    }

    [Fact]
    public void DifferentSources_ProduceDifferentSourcePrefixes()
    {
        var key1 = CacheKey.FromStrings("/images/a.jpg", "width=400");
        var key2 = CacheKey.FromStrings("/images/b.jpg", "width=400");

        Assert.NotEqual(key1.SourcePrefix(), key2.SourcePrefix());
    }

    [Fact]
    public void ToStoragePath_HasThreeLevels()
    {
        var key = CacheKey.FromStrings("/images/photo.jpg", "width=400");
        var path = key.ToStoragePath();

        var parts = path.Split('/');
        Assert.Equal(3, parts.Length);
        Assert.Equal(4, parts[0].Length);  // 4-char fan-out prefix
        Assert.Equal(32, parts[1].Length); // 32-char source hex
        Assert.Equal(32, parts[2].Length); // 32-char variant hex
    }

    [Fact]
    public void SourcePrefix_Is32HexChars()
    {
        var key = CacheKey.FromStrings("test", "variant");
        var prefix = key.SourcePrefix();

        Assert.Equal(32, prefix.Length);
        Assert.True(prefix.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void HashCode_IsConsistent()
    {
        var key1 = CacheKey.FromStrings("source", "variant");
        var key2 = CacheKey.FromStrings("source", "variant");

        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
    }

    [Fact]
    public void Equality_OperatorsWork()
    {
        var key1 = CacheKey.FromStrings("a", "b");
        var key2 = CacheKey.FromStrings("a", "b");
        var key3 = CacheKey.FromStrings("a", "c");

        Assert.True(key1 == key2);
        Assert.False(key1 != key2);
        Assert.True(key1 != key3);
        Assert.False(key1 == key3);
    }
}
