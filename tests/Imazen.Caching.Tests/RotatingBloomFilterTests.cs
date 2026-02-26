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

    [Fact]
    public void ToBytes_LoadFromBytes_RoundTrip()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01, slotCount: 2);

        for (int i = 0; i < 100; i++)
            bloom.Insert($"roundtrip-{i}");

        var bytes = bloom.ToBytes();

        // Load into a new filter with same parameters
        var bloom2 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        bloom2.LoadFromBytes(bytes);

        // All inserted keys should be found
        for (int i = 0; i < 100; i++)
            Assert.True(bloom2.ProbablyContains($"roundtrip-{i}"), $"Lost key roundtrip-{i}");

        // Keys never inserted should still be absent
        for (int i = 0; i < 100; i++)
            Assert.False(bloom2.ProbablyContains($"never-inserted-{i}"));
    }

    [Fact]
    public void ToBytes_HasCorrectHeader()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01, slotCount: 3);
        var bytes = bloom.ToBytes();

        // Magic "BF01"
        Assert.Equal(0x42, bytes[0]);
        Assert.Equal(0x46, bytes[1]);
        Assert.Equal(0x30, bytes[2]);
        Assert.Equal(0x31, bytes[3]);

        // Version
        Assert.Equal(1, bytes[4]);

        // Slot count
        Assert.Equal(3, bytes[5]);

        // Total size matches SerializedSizeBytes
        Assert.Equal(bloom.SerializedSizeBytes, bytes.Length);
    }

    [Fact]
    public void LoadFromBytes_IncompatibleParameters_Throws()
    {
        var bloom1 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        var bloom2 = new RotatingBloomFilter(2000, 0.01, slotCount: 2);

        var bytes = bloom1.ToBytes();

        // Different bitsPerSlot → throws
        Assert.Throws<ArgumentException>(() => bloom2.LoadFromBytes(bytes));
    }

    [Fact]
    public void LoadFromBytes_DifferentSlotCount_Throws()
    {
        var bloom1 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        var bloom2 = new RotatingBloomFilter(1000, 0.01, slotCount: 4);

        var bytes = bloom1.ToBytes();
        Assert.Throws<ArgumentException>(() => bloom2.LoadFromBytes(bytes));
    }

    [Fact]
    public void LoadFromBytes_InvalidMagic_Throws()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01);
        var bytes = bloom.ToBytes();
        bytes[0] = 0xFF; // corrupt magic
        Assert.Throws<ArgumentException>(() => bloom.LoadFromBytes(bytes));
    }

    [Fact]
    public void MergeFrom_UnionOfBothFilters()
    {
        var bloom1 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        var bloom2 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);

        // Insert different keys into each
        for (int i = 0; i < 50; i++)
            bloom1.Insert($"node1-key-{i}");
        for (int i = 0; i < 50; i++)
            bloom2.Insert($"node2-key-{i}");

        // Before merge: each only knows its own keys
        Assert.True(bloom1.ProbablyContains("node1-key-0"));
        Assert.False(bloom1.ProbablyContains("node2-key-0"));

        // OR merge
        bloom1.MergeFrom(bloom2);

        // After merge: bloom1 knows both sets
        for (int i = 0; i < 50; i++)
        {
            Assert.True(bloom1.ProbablyContains($"node1-key-{i}"), $"Lost node1 key {i}");
            Assert.True(bloom1.ProbablyContains($"node2-key-{i}"), $"Missing node2 key {i}");
        }
    }

    [Fact]
    public void MergeFromBytes_WorksWithSerializedData()
    {
        var bloom1 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        var bloom2 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);

        for (int i = 0; i < 50; i++)
            bloom1.Insert($"local-{i}");
        for (int i = 0; i < 50; i++)
            bloom2.Insert($"remote-{i}");

        // Serialize bloom2, merge into bloom1 without constructing a filter
        var peerData = bloom2.ToBytes();
        bloom1.MergeFromBytes(peerData);

        // bloom1 now has union
        for (int i = 0; i < 50; i++)
        {
            Assert.True(bloom1.ProbablyContains($"local-{i}"));
            Assert.True(bloom1.ProbablyContains($"remote-{i}"));
        }
    }

    [Fact]
    public void MergeFrom_IncompatibleParameters_Throws()
    {
        var bloom1 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        var bloom2 = new RotatingBloomFilter(2000, 0.01, slotCount: 2);

        Assert.Throws<ArgumentException>(() => bloom1.MergeFrom(bloom2));
    }

    [Fact]
    public void SerializeAfterRotation_PreservesState()
    {
        var bloom = new RotatingBloomFilter(1000, 0.01, slotCount: 2);

        bloom.Insert("before-rotate");
        bloom.Rotate();
        bloom.Insert("after-rotate");

        var bytes = bloom.ToBytes();
        var bloom2 = new RotatingBloomFilter(1000, 0.01, slotCount: 2);
        bloom2.LoadFromBytes(bytes);

        // "before-rotate" is in slot 0 (now old), "after-rotate" is in slot 1 (current)
        // Both should survive serialization
        Assert.True(bloom2.ProbablyContains("before-rotate"));
        Assert.True(bloom2.ProbablyContains("after-rotate"));
    }

    [Fact]
    public void SerializedSizeBytes_MatchesActualOutput()
    {
        var bloom = new RotatingBloomFilter(10000, 0.01, slotCount: 4);
        Assert.Equal(bloom.SerializedSizeBytes, bloom.ToBytes().Length);
    }
}
