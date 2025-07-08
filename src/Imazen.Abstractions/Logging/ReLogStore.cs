using System.Collections.Concurrent;
using System.Text;
using Imazen.Abstractions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;

namespace Imazen.Abstractions.Logging
{
    public class ReLogStore : IReLogStore
    {
        private readonly ReLogStoreOptions options;
        private readonly ConcurrentDictionary<ulong, LogEntryGroup> logEntries = new ConcurrentDictionary<ulong, LogEntryGroup>();
        public ReLogStore(ReLogStoreOptions options)
        {
            this.options = options; 
        }
        public void Log<TState>(string categoryName, KeyValuePair<string,object>[]? scopeData, Stack<IDisposable>? scopeStack, LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter, bool retain, string? retainUniqueKey)
        {
            // Check if it meets the criteria
            // If so, store it
            if (!retain) return;

            if (logEntries.Count > options.MaxEventGroups)
            {
                return;
            }

            // hash categoryName, exception class (if present), and (retainUniqueKey) if present.
            var hash = Fnv1AHash.Create();
            hash.Add(categoryName);
            hash.Add((int)logLevel);
            var exceptionType = exception?.GetType().GetHashCode();
            hash.Add(exceptionType?.GetHashCode() ?? 0);

            string? scopeString = null;

            if (retainUniqueKey == null && exceptionType == null)
            {
                scopeString = GetScopeString(scopeStack, scopeData);
                //TOOD: work around this alloc
                hash.Add(state?.ToString());
                // loop scope stack and add to hash
                hash.Add(scopeString);
            }
            else
            {
                hash.Add(retainUniqueKey ?? "");
            }
            
            // TODO: later, we can update them periodically
            var hashKey = hash.CurrentHash;
            var entries = logEntries.GetOrAdd(hashKey, new LogEntryGroup(hashKey, logLevel));
            if (retainUniqueKey != null)
            {
                if (entries.Count > options.MaxEntriesPerUniqueKey)
                {
                    entries.IncrementEntriesNotAdded();
                    return;
                }
            }
            else
            {
                if (entries.Count > options.MaxEntriesPerExceptionClass)
                {
                    entries.IncrementEntriesNotAdded();
                    return;
                }
            }
            scopeString ??= GetScopeString(scopeStack, scopeData);
       
            entries.Add(new LogEntry()
            {
                CategoryName = categoryName,
                ScopeString = scopeString,
                LogLevel = logLevel,
                EventId = eventId,
                Message = formatter(state, exception),
                RetainUniqueKey = retainUniqueKey,
                Timestamp = DateTimeOffset.UtcNow
            });
            
        }

        public string GetReport(ReLogStoreReportOptions reportOptions)
        {
            // Create a list of all groups, sort by loglevel and count
            var groups = logEntries.Values.OrderByDescending(g => g.LogLevel).ThenByDescending(g => g.TotalEntries).ToList();
            var sb = new StringBuilder();
            // Showing {x} of {y} log entries marked for retention (z groups)
            sb.Append($"Showing {groups.Sum(g => g.TotalEntries)} of {logEntries.Count} log entries marked for retention ({groups.Count} groups)\n");
            foreach (var group in groups)
            {
                // (totalCount Error) 2020-01-01 00:00:00.000 CategoryName ScopeString Message EventId
                foreach (var entry in group.OrderByDescending(e => e.Timestamp))
                {
                    entry.Format(sb, group.TotalEntries, true);
                    sb.Append("\n");
                }
            }

            return sb.ToString();
        }
        public static string? GetScopeString(Stack<IDisposable>? scopeStack, KeyValuePair<string, object>[]? scopeData)
        {
            if (scopeStack == null) return null;
            var sb = new StringBuilder();
            if (scopeData != null)
            {
                foreach (var scope in scopeData)
                {
                    sb.Append(scope.Key);
                    sb.Append("=");
                    sb.Append(scope.Value);
                    sb.Append(">");
                }
            }
            sb.Append(" ");
            foreach (var scope in scopeStack)
            {
                sb.Append(scope.ToString());
                sb.Append(">");
            }
            if (sb.Length > 0) sb.Length--;
            return sb.ToString();
        }
    }


    internal class LogEntryGroup : ConcurrentBag<LogEntry>
    {
        public LogEntryGroup(ulong groupHash, LogLevel logLevel) : base()
        {
            GroupHash = groupHash;
            this.LogLevel = logLevel;
        }

        public LogLevel LogLevel { get; }
        private ulong GroupHash { get; }
        private int entriesNotAdded;
        public int EntriesNotAdded => entriesNotAdded;
        
        public int TotalEntries => entriesNotAdded + this.Count;
        public void IncrementEntriesNotAdded()
        {
            entriesNotAdded++;
        }
    }

    internal struct LogEntry
    {
        public string CategoryName { get; set; }

        public string? ScopeString { get; set; }
        public LogLevel LogLevel { get; set; }
        public EventId EventId { get; set; }
        public string Message { get; set; }
        public string? RetainUniqueKey { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        
        public void Format(StringBuilder sb, int totalCount, bool relativeTime)
        {
            // (totalCount Error) 2020-01-01 00:00:00.000 CategoryName ScopeString Message EventId
            sb.EnsureCapacity(sb.Length + 100);
            sb.Append('(');
            sb.Append(totalCount);
            sb.Append(' ');
            sb.Append(LogLevel);
            sb.Append(") ");
            if (relativeTime)
            {
                sb.Append((Timestamp - DateTimeOffset.UtcNow).ToString("hh\\:mm\\:ss"));
                sb.Append("s ago");
            }
            else
            {
                sb.Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }

            sb.Append(' ');
            sb.Append(CategoryName);
            if (ScopeString != null)
            {
                sb.Append(' ');
                sb.Append(ScopeString);
            }
            sb.Append(' ');
            sb.Append(Message);
            sb.Append(' ');
            sb.Append(EventId);
            
        }
    }
}