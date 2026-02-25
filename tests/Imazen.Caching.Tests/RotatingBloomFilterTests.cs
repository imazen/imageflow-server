using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class RotatingBloomFilterTests
{
    [Fact]
    public void Insert_ThenProbablyContains_ReturnsTrue()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01);

        bloom.Insert("test-key");

        Assert.True(bloom.ProbablyContains("test-key"));
    }

    [Fact]
    public void NotInserted_ProbablyContains_ReturnsFalse()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01);

        // Not inserted — should return false (with high probability)
        Assert.False(bloom.ProbablyContains("never-inserted-key"));
    }

    [Fact]
    public void ManyInserts_NoFalseNegatives()
    {
        var bloom = new RotatingBloomFilter(10000, 0.01);

        var keys = Enumerable.Range(0, 1000).Select(i => $"key-{i}").ToList();

        foreach (var key in keys)
            bloom.Insert(key);

        // No false negatives: every inserted key must be found
        foreach (var key in keys)
            Assert.True(bloom.ProbablyContains(key), $"False negative for {key}");
    }

    [Fact]
    public void FalsePositiveRate_WithinBounds()
    {
        var bloom = new RotatingBloomFilter(10000, 0.01);

        // Insert 10000 keys
        for (int i = 0; i < 10000; i++)
            bloom.Insert($"inserted-{i}");

        // Check 10000 DIFFERENT keys for false positives
        int falsePositives = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (bloom.ProbablyContains($"not-inserted-{i}"))
                falsePositives++;
        }

        double fpRate = (double)falsePositives / 10000;
        // Allow 5x the target rate for statistical variance
        Assert.True(fpRate < 0.05, $"False positive rate {fpRate:P2} exceeds 5% (target was 1%)");
    }

    [Fact]
    public void Rotate_OldEntriesAgeOut()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01, slotCount: 2);

        bloom.Insert("old-key");
        Assert.True(bloom.ProbablyContains("old-key"));

        // Rotate twice (2 slots total) — should clear the slot containing "old-key"
        bloom.Rotate();
        bloom.Rotate();

        // After rotating through all slots, old key should be gone
        Assert.False(bloom.ProbablyContains("old-key"));
    }

    [Fact]
    public void Rotate_NewInsertsSurviveOneRotation()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01, slotCount: 4);

        bloom.Insert("recent-key");

        // One rotation — recent key should still be findable (it's in a different slot)
        bloom.Rotate();
        Assert.True(bloom.ProbablyContains("recent-key"));
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01);

        for (int i = 0; i < 100; i++)
            bloom.Insert($"key-{i}");

        bloom.Clear();

        for (int i = 0; i < 100; i++)
            Assert.False(bloom.ProbablyContains($"key-{i}"));
    }

    [Fact]
    public void EstimatedMemory_IsReasonable()
    {
        // 10M items at 1% FPR, 4 slots
        var bloom = new RotatingBloomFilter(10_000_000, 0.01, slotCount: 4);

        // Should be ~46MB (11.5MB per slot * 4)
        var memMb = bloom.EstimatedMemoryBytes / (1024.0 * 1024.0);
        Assert.True(memMb > 30, $"Memory too low: {memMb:F1}MB");
        Assert.True(memMb < 80, $"Memory too high: {memMb:F1}MB");
    }

    [Fact]
    public void Constructor_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingBloomFilter(0, 0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingBloomFilter(1000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingBloomFilter(1000, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingBloomFilter(1000, 0.01, 0));
    }

    [Fact]
    public void ConcurrentInserts_NoExceptions()
    {
        var bloom = new RotatingBloomFilter(100000, 0.01);

        Parallel.For(0, 10000, i =>
        {
            bloom.Insert($"concurrent-key-{i}");
        });

        // Verify all were inserted
        int found = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (bloom.ProbablyContains($"concurrent-key-{i}"))
                found++;
        }

        // All inserted keys must be found (no false negatives)
        Assert.Equal(10000, found);
    }
}
