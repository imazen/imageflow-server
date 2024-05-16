using Imazen.Abstractions.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Imazen.Routing.Tests.Serving;

public class MockHelpers
{
    
    public static IReLoggerFactory MakeNullLoggerFactory(IReLogStore? logStore)
    {
        logStore ??= new ReLogStore(new ReLogStoreOptions());
        var nullLogger = new NullLoggerFactory();
        return new ReLoggerFactory(nullLogger, logStore);
    }
    
    public static IReLoggerFactory MakeMemoryLoggerFactory(List<MemoryLogEntry> logList,
        IReLogStore? logStore = null)
    {
        logStore ??= new ReLogStore(new ReLogStoreOptions());
        var logger = new MemoryLoggerFactory(LogLevel.Trace, logList);
        return new ReLoggerFactory(logger, logStore);
    }
}