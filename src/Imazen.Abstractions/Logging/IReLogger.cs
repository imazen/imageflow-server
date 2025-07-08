using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Logging
{
    /// <summary>
    /// An ILogger instance capable of retaining information in memory for later recall.
    /// Call .Retain. to enter retain mode. 
    /// </summary>
    public interface IReLogger : ILogger
    {
        /// <summary>
        /// Provides a logger that retains log entries according to app configuration
        /// </summary>
        IReLogger WithRetain { get; }

        /// <summary>
        /// Provides a logger that will retain only unique log entries with the given key.
        /// key does not need to include the category hierarchy or log level.
        /// If specified, it will be used in place of (Exception.GetType().GetHashCode() if present, which falls back to scope hierarchy + args-to-formatter)
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        IReLogger WithRetainUnique(string key);
        
        /// <summary>
        /// Provides a new logger that will append the given string to the current category name
        /// </summary>
        /// <param name="subcategoryString"></param>
        /// <returns></returns>
        IReLogger WithSubcategory(string subcategoryString);
        
        IReLogger WithReScopeData(string key, object value);
        IReLogger WithReScopeData(KeyValuePair<string, object>[] pairs);
    }

    public interface IReLogger<T> : IReLogger
    {
    }
}