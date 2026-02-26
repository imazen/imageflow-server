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
/// Supports serialization (ToBytes/LoadFromBytes) for persistence and
/// OR merge (MergeFrom) for cluster synchronization.
///
/// Memory usage: ~(estimatedItems * -ln(fpRate) / (ln(2)^2)) bits per slot.
/// At 10M items, 1% FPR: ~11.5MB per slot, ~46MB for 4 slots.
/// </summary>
public sealed class RotatingBloomFilter
{
    // Serialization header: 16 bytes
    // [0..3]   magic "BF01"
    // [4]      version (1)
    // [5]      slotCount
    // [6..7]   numHashFunctions (little-endian uint16)
    // [8..11]  bitsPerSlot (little-endian int32)
    // [12..15] reserved (zeroes)
    private static readonly byte[] Magic = { 0x42, 0x46, 0x30, 0x31 }; // "BF01"
    private const int HeaderSize = 16;
    private const byte FormatVersion = 1;

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

    /// <summary>
    /// Serialize the bloom filter to a byte array.
    /// Format: 16-byte header + all slots' int[] data as raw bytes.
    /// The header encodes parameters so the receiver can validate compatibility.
    /// </summary>
    public byte[] ToBytes()
    {
        int intsPerSlot = (_bitsPerSlot + 31) / 32;
        int bytesPerSlot = intsPerSlot * 4;
        int totalSize = HeaderSize + _slotCount * bytesPerSlot;
        var result = new byte[totalSize];

        // Write header
        result[0] = Magic[0];
        result[1] = Magic[1];
        result[2] = Magic[2];
        result[3] = Magic[3];
        result[4] = FormatVersion;
        result[5] = (byte)_slotCount;
        result[6] = (byte)(_numHashFunctions & 0xFF);
        result[7] = (byte)((_numHashFunctions >> 8) & 0xFF);
        result[8] = (byte)(_bitsPerSlot & 0xFF);
        result[9] = (byte)((_bitsPerSlot >> 8) & 0xFF);
        result[10] = (byte)((_bitsPerSlot >> 16) & 0xFF);
        result[11] = (byte)((_bitsPerSlot >> 24) & 0xFF);
        // [12..15] reserved

        // Write slots
        for (int s = 0; s < _slotCount; s++)
        {
            Buffer.BlockCopy(_slots[s], 0, result, HeaderSize + s * bytesPerSlot, bytesPerSlot);
        }

        return result;
    }

    /// <summary>
    /// Load bloom filter state from a serialized byte array.
    /// The byte array must have been produced by ToBytes() with identical parameters
    /// (same bitsPerSlot, numHashFunctions, slotCount).
    /// Replaces all current slot data.
    /// </summary>
    public void LoadFromBytes(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < HeaderSize)
            throw new ArgumentException("Data too short to contain bloom filter header");

        // Validate header
        if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3])
            throw new ArgumentException("Invalid bloom filter magic bytes");
        if (data[4] != FormatVersion)
            throw new ArgumentException($"Unsupported bloom filter format version {data[4]}, expected {FormatVersion}");

        int storedSlotCount = data[5];
        int storedHashFunctions = data[6] | (data[7] << 8);
        int storedBitsPerSlot = data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24);

        if (storedSlotCount != _slotCount)
            throw new ArgumentException($"Slot count mismatch: data has {storedSlotCount}, filter has {_slotCount}");
        if (storedHashFunctions != _numHashFunctions)
            throw new ArgumentException($"Hash function count mismatch: data has {storedHashFunctions}, filter has {_numHashFunctions}");
        if (storedBitsPerSlot != _bitsPerSlot)
            throw new ArgumentException($"Bits per slot mismatch: data has {storedBitsPerSlot}, filter has {_bitsPerSlot}");

        int intsPerSlot = (_bitsPerSlot + 31) / 32;
        int bytesPerSlot = intsPerSlot * 4;
        int expectedSize = HeaderSize + _slotCount * bytesPerSlot;

        if (data.Length != expectedSize)
            throw new ArgumentException($"Data size mismatch: expected {expectedSize}, got {data.Length}");

        lock (_writeLock)
        {
            for (int s = 0; s < _slotCount; s++)
            {
                Buffer.BlockCopy(data, HeaderSize + s * bytesPerSlot, _slots[s], 0, bytesPerSlot);
            }
        }
    }

    /// <summary>
    /// OR-merge another bloom filter's data into this one.
    /// Each slot is merged with the corresponding slot from the other filter
    /// using lock-free CAS operations. After merge, this filter contains the
    /// union of both filters' entries.
    ///
    /// Used for cluster synchronization: if any node saw a key, all nodes
    /// should know about it. Items that were evicted from cloud but are wanted
    /// again will reappear shortly, so the perf cost of stale positives is minimal.
    /// </summary>
    public void MergeFrom(RotatingBloomFilter other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        if (other._bitsPerSlot != _bitsPerSlot)
            throw new ArgumentException($"Bits per slot mismatch: other has {other._bitsPerSlot}, this has {_bitsPerSlot}");
        if (other._slotCount != _slotCount)
            throw new ArgumentException($"Slot count mismatch: other has {other._slotCount}, this has {_slotCount}");
        if (other._numHashFunctions != _numHashFunctions)
            throw new ArgumentException($"Hash function count mismatch: other has {other._numHashFunctions}, this has {_numHashFunctions}");

        for (int s = 0; s < _slotCount; s++)
        {
            MergeSlot(_slots[s], other._slots[s]);
        }
    }

    /// <summary>
    /// OR-merge from a serialized byte array without constructing a full RotatingBloomFilter.
    /// Validates the header for compatibility, then merges slot data directly.
    /// </summary>
    public void MergeFromBytes(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length < HeaderSize)
            throw new ArgumentException("Data too short to contain bloom filter header");

        // Validate header
        if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3])
            throw new ArgumentException("Invalid bloom filter magic bytes");
        if (data[4] != FormatVersion)
            throw new ArgumentException($"Unsupported bloom filter format version {data[4]}, expected {FormatVersion}");

        int storedSlotCount = data[5];
        int storedHashFunctions = data[6] | (data[7] << 8);
        int storedBitsPerSlot = data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24);

        if (storedSlotCount != _slotCount)
            throw new ArgumentException($"Slot count mismatch: data has {storedSlotCount}, filter has {_slotCount}");
        if (storedHashFunctions != _numHashFunctions)
            throw new ArgumentException($"Hash function count mismatch: data has {storedHashFunctions}, filter has {_numHashFunctions}");
        if (storedBitsPerSlot != _bitsPerSlot)
            throw new ArgumentException($"Bits per slot mismatch: data has {storedBitsPerSlot}, filter has {_bitsPerSlot}");

        int intsPerSlot = (_bitsPerSlot + 31) / 32;
        int bytesPerSlot = intsPerSlot * 4;

        if (data.Length != HeaderSize + _slotCount * bytesPerSlot)
            throw new ArgumentException($"Data size mismatch");

        // Merge each slot from raw bytes
        var otherSlot = new int[intsPerSlot];
        for (int s = 0; s < _slotCount; s++)
        {
            Buffer.BlockCopy(data, HeaderSize + s * bytesPerSlot, otherSlot, 0, bytesPerSlot);
            MergeSlot(_slots[s], otherSlot);
        }
    }

    /// <summary>
    /// OR-merge otherSlot into targetSlot using lock-free CAS.
    /// </summary>
    private static void MergeSlot(int[] targetSlot, int[] otherSlot)
    {
        for (int i = 0; i < targetSlot.Length; i++)
        {
            int otherValue = Volatile.Read(ref otherSlot[i]);
            if (otherValue == 0) continue;

#if NET8_0_OR_GREATER
            Interlocked.Or(ref targetSlot[i], otherValue);
#else
            int oldVal, newVal;
            do
            {
                oldVal = Volatile.Read(ref targetSlot[i]);
                newVal = oldVal | otherValue;
                if (oldVal == newVal) break;
            } while (Interlocked.CompareExchange(ref targetSlot[i], newVal, oldVal) != oldVal);
#endif
        }
    }

    /// <summary>
    /// Returns the serialized size in bytes (header + all slot data).
    /// </summary>
    public int SerializedSizeBytes
    {
        get
        {
            int intsPerSlot = (_bitsPerSlot + 31) / 32;
            return HeaderSize + _slotCount * intsPerSlot * 4;
        }
    }

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
