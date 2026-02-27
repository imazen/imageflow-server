using System;
using System.Security.Cryptography;
using System.Text;

namespace Imazen.Caching;

/// <summary>
/// A two-part cache key: source hash (identifies the original resource) and
/// variant hash (identifies the specific transformation/parameters).
/// The source hash enables purge-by-source via prefix listing.
/// The variant hash ensures uniqueness per parameter combination.
/// </summary>
public readonly struct CacheKey : IEquatable<CacheKey>
{
    /// <summary>
    /// First 16 bytes of SHA256 of the source identifier (virtual path, URL, etc.)
    /// </summary>
    public ReadOnlyMemory<byte> SourceHash { get; }

    /// <summary>
    /// First 16 bytes of SHA256 of the full cache key (source + parameters)
    /// </summary>
    public ReadOnlyMemory<byte> VariantHash { get; }

    public CacheKey(ReadOnlyMemory<byte> sourceHash, ReadOnlyMemory<byte> variantHash)
    {
        if (sourceHash.Length != 16)
            throw new ArgumentException("Source hash must be 16 bytes", nameof(sourceHash));
        if (variantHash.Length != 16)
            throw new ArgumentException("Variant hash must be 16 bytes", nameof(variantHash));
        SourceHash = sourceHash;
        VariantHash = variantHash;
    }

    /// <summary>
    /// Creates a CacheKey from a raw 32-byte hash by splitting into two 16-byte halves.
    /// First 16 bytes → source hash, last 16 → variant hash.
    /// Note: purge-by-source won't work meaningfully with keys created this way
    /// since the first 16 bytes aren't a pure source hash.
    /// Use FromStrings for proper source tracking.
    /// </summary>
    public static CacheKey FromRaw32(byte[] hash32)
    {
        if (hash32 == null) throw new ArgumentNullException(nameof(hash32));
        if (hash32.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes", nameof(hash32));

        var sourceHash = new byte[16];
        var variantHash = new byte[16];
        Buffer.BlockCopy(hash32, 0, sourceHash, 0, 16);
        Buffer.BlockCopy(hash32, 16, variantHash, 0, 16);
        return new CacheKey(sourceHash, variantHash);
    }

    /// <summary>
    /// Creates a CacheKey from string identifiers. The source is hashed to produce
    /// the source prefix, and source+variant together produce the variant hash.
    /// </summary>
    public static CacheKey FromStrings(string source, string variant)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var variantBytes = Encoding.UTF8.GetBytes(source + "\0" + variant);

#if NET8_0_OR_GREATER
        var sourceHash = SHA256.HashData(sourceBytes).AsMemory(0, 16);
        var variantHash = SHA256.HashData(variantBytes).AsMemory(0, 16);
#else
        byte[] sourceHash;
        byte[] variantHash;
        using (var sha = SHA256.Create())
        {
            var fullSourceHash = sha.ComputeHash(sourceBytes);
            sourceHash = new byte[16];
            Buffer.BlockCopy(fullSourceHash, 0, sourceHash, 0, 16);

            var fullVariantHash = sha.ComputeHash(variantBytes);
            variantHash = new byte[16];
            Buffer.BlockCopy(fullVariantHash, 0, variantHash, 0, 16);
        }
#endif
        return new CacheKey(sourceHash, variantHash);
    }

    /// <summary>
    /// Returns a hex-encoded string of the source hash (32 hex chars).
    /// Used as a prefix for purge-by-source operations.
    /// </summary>
    public string SourcePrefix()
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(SourceHash.Span).ToLowerInvariant();
#else
        return BytesToHex(SourceHash.Span);
#endif
    }

    /// <summary>
    /// Returns a storage path: {sourceHex[0..3]}/{sourceHex}/{variantHex}
    /// The first directory level provides ~65K-way fan-out for filesystem storage.
    /// </summary>
    public string ToStoragePath()
    {
#if NET8_0_OR_GREATER
        var sourceHex = Convert.ToHexString(SourceHash.Span).ToLowerInvariant();
        var variantHex = Convert.ToHexString(VariantHash.Span).ToLowerInvariant();
#else
        var sourceHex = BytesToHex(SourceHash.Span);
        var variantHex = BytesToHex(VariantHash.Span);
#endif
        return $"{sourceHex.Substring(0, 4)}/{sourceHex}/{variantHex}";
    }

    /// <summary>
    /// A string key combining both hashes, suitable for dictionary lookups and coalescing.
    /// </summary>
    public string ToStringKey()
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(SourceHash.Span).ToLowerInvariant() + ":" +
               Convert.ToHexString(VariantHash.Span).ToLowerInvariant();
#else
        return BytesToHex(SourceHash.Span) + ":" + BytesToHex(VariantHash.Span);
#endif
    }

#if !NET8_0_OR_GREATER
    private static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }
#endif

    public bool Equals(CacheKey other)
    {
        return SourceHash.Span.SequenceEqual(other.SourceHash.Span) &&
               VariantHash.Span.SequenceEqual(other.VariantHash.Span);
    }

    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

    public override int GetHashCode()
    {
        var span = VariantHash.Span;
        if (span.Length >= 4)
        {
            return span[0] | (span[1] << 8) | (span[2] << 16) | (span[3] << 24);
        }
        return 0;
    }

    public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);
    public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);

    public override string ToString() => ToStringKey();
}
