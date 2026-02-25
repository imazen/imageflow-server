using System;
using System.Threading;

namespace Imazen.Caching;

/// <summary>
/// A rotating bloom filter with multiple slots. New inserts go to the current slot.
/// Lookups check all slots. Periodically rotating (clearing the oldest slot) bounds
/// memory and allows stale entries to age out.
///
/// Thread-safe via lock-free reads (bitwise OR across slots) and lock on rotation/insert.
///
/// Memory usage: ~(estimatedItems * -ln(fpRate) / (ln(2)^2)) bits per slot.
/// At 10M items, 1% FPR: ~11.5MB per slot, ~46MB for 4 slots.
/// </summary>
public sealed class RotatingBloomFilter
{
    private readonly int _numHashFunctions;
    private readonly int _bitsPerSlot;
    private readonly int[][] _slots; // each slot is an int[] used as a bit array
    private readonly int _slotCount;
    private int _currentSlot;
    private readonly object _writeLock = new();

    /// <summary>
    /// Create a bloom filter sized for the given parameters.
    /// </summary>
    /// <param name="estimatedItems">Expected number of items.</param>
    /// <param name="falsePositiveRate">Target false positive rate (0..1).</param>
    /// <param name="slotCount">Number of rotating slots.</param>
    public RotatingBloomFilter(int estimatedItems, double falsePositiveRate, int slotCount = 4)
    {
        if (estimatedItems <= 0) throw new ArgumentOutOfRangeException(nameof(estimatedItems));
        if (falsePositiveRate <= 0 || falsePositiveRate >= 1) throw new ArgumentOutOfRangeException(nameof(falsePositiveRate));
        if (slotCount < 1) throw new ArgumentOutOfRangeException(nameof(slotCount));

        _slotCount = slotCount;

        // Optimal bits: m = -n * ln(p) / (ln(2)^2)
        double ln2Sq = Math.Log(2) * Math.Log(2);
        long optimalBits = (long)Math.Ceiling(-estimatedItems * Math.Log(falsePositiveRate) / ln2Sq);

        // Cap at 256MB per slot (2 billion bits)
        optimalBits = Math.Min(optimalBits, 2_000_000_000L);
        _bitsPerSlot = (int)optimalBits;

        // Optimal hash functions: k = (m/n) * ln(2)
        _numHashFunctions = Math.Max(1, (int)Math.Round((double)_bitsPerSlot / estimatedItems * Math.Log(2)));

        // Allocate slots
        int intsPerSlot = (_bitsPerSlot + 31) / 32;
        _slots = new int[_slotCount][];
        for (int i = 0; i < _slotCount; i++)
        {
            _slots[i] = new int[intsPerSlot];
        }
    }

    /// <summary>
    /// Insert a key into the current slot.
    /// </summary>
    public void Insert(string key)
    {
        int h1 = GetHash1(key);
        int h2 = GetHash2(key);
        var slot = _slots[_currentSlot];

        for (int i = 0; i < _numHashFunctions; i++)
        {
            int bit = GetBitIndex(h1, h2, i);
            int wordIndex = bit >> 5;
            int bitMask = 1 << (bit & 31);

            // Lock-free set via Interlocked.Or
#if NET8_0_OR_GREATER
            Interlocked.Or(ref slot[wordIndex], bitMask);
#else
            // Spin-CAS for netstandard2.0
            int oldVal, newVal;
            do
            {
                oldVal = Volatile.Read(ref slot[wordIndex]);
                newVal = oldVal | bitMask;
                if (oldVal == newVal) break;
            } while (Interlocked.CompareExchange(ref slot[wordIndex], newVal, oldVal) != oldVal);
#endif
        }
    }

    /// <summary>
    /// Check if a key is probably in any slot. Returns false only if the key is
    /// definitely not present.
    /// </summary>
    public bool ProbablyContains(string key)
    {
        int h1 = GetHash1(key);
        int h2 = GetHash2(key);

        // Check all slots (any slot containing the key means "probably yes")
        for (int s = 0; s < _slotCount; s++)
        {
            if (SlotContains(_slots[s], h1, h2))
                return true;
        }
        return false;
    }

    private bool SlotContains(int[] slot, int h1, int h2)
    {
        for (int i = 0; i < _numHashFunctions; i++)
        {
            int bit = GetBitIndex(h1, h2, i);
            int wordIndex = bit >> 5;
            int bitMask = 1 << (bit & 31);

            if ((Volatile.Read(ref slot[wordIndex]) & bitMask) == 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Rotate: clear the oldest slot and advance the current pointer.
    /// This ages out stale entries over time.
    /// </summary>
    public void Rotate()
    {
        lock (_writeLock)
        {
            int nextSlot = (_currentSlot + 1) % _slotCount;
            Array.Clear(_slots[nextSlot], 0, _slots[nextSlot].Length);
            _currentSlot = nextSlot;
        }
    }

    /// <summary>
    /// Clear all slots. Useful for testing.
    /// </summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            for (int i = 0; i < _slotCount; i++)
            {
                Array.Clear(_slots[i], 0, _slots[i].Length);
            }
        }
    }

    /// <summary>
    /// Estimated memory usage in bytes across all slots.
    /// </summary>
    public long EstimatedMemoryBytes
    {
        get
        {
            long intsPerSlot = ((long)_bitsPerSlot + 31) / 32;
            return intsPerSlot * 4 * _slotCount;
        }
    }

    /// <summary>
    /// Number of bits per slot.
    /// </summary>
    public int BitsPerSlot => _bitsPerSlot;

    /// <summary>
    /// Number of hash functions used.
    /// </summary>
    public int HashFunctionCount => _numHashFunctions;

    // Double hashing: h(i) = h1 + i*h2 (mod m)
    // Uses FNV-1a variants for two independent hash functions.

    private int GetBitIndex(int h1, int h2, int i)
    {
        // Ensure positive via masking
        long combined = ((long)(h1 & 0x7FFFFFFF) + (long)i * (h2 & 0x7FFFFFFF)) % _bitsPerSlot;
        return (int)combined;
    }

    private static int GetHash1(string key)
    {
        // FNV-1a 32-bit
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private static int GetHash2(string key)
    {
        // Modified FNV with different seed/prime
        unchecked
        {
            uint hash = 2654435761; // Knuth's multiplicative constant
            for (int i = 0; i < key.Length; i++)
            {
                hash *= (uint)(1099511628211UL & 0xFFFFFFFF); // Lower 32 bits of FNV-1a 64-bit prime
                hash ^= key[i];
            }
            // Ensure non-zero for double hashing
            return (int)(hash | 1);
        }
    }
}
