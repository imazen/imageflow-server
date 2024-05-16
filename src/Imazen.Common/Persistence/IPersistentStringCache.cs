namespace Imazen.Common.Persistence
{
    /// <summary>
    /// The result of the IPersistentStringCache cache write; internal use only.
    /// </summary>
    public enum StringCachePutResult
    {
        /// <summary>
        /// The in-memory copy is exactly the same; write skipped
        /// </summary>
        Duplicate,
        /// <summary>
        /// Write succeeded
        /// </summary>
        WriteComplete,
        /// <summary>
        /// An error occurred. Check the error sink on the implementation
        /// </summary>
        WriteFailed
    }

    /// <summary>
    ///  Implementations must not be tied or reliant on a specific Config instance
    /// </summary>
    internal interface IPersistentStringCache
    {
        StringCachePutResult TryPut(string key, string value);
        string? Get(string key);


        DateTime? GetWriteTimeUtc(string key);
    }
}
