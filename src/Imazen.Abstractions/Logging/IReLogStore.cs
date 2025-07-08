using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Logging
{
    public interface IReLogStore
    {
        void Log<TState>(string categoryName,KeyValuePair<string, object>[]? reScopeData, Stack<IDisposable>? scopeStack, LogLevel logLevel, EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter, bool retain,
            string? retainUniqueKey);

        public string GetReport(ReLogStoreReportOptions options);
    }
}