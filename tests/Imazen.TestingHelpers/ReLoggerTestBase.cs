using Imazen.Abstractions.Logging;
using Imazen.Routing.Tests.Serving;

namespace Imazen.Tests.Routing.Serving;

public class ReLoggerTestBase
{
    protected readonly IReLoggerFactory loggerFactory;
    protected readonly IReLogger logger;
    protected readonly IReLogStore logStore;
    protected readonly List<MemoryLogEntry> logList;


    public ReLoggerTestBase(string categoryName)
    {
        logList = new List<MemoryLogEntry>();
        logStore = new ReLogStore(new ReLogStoreOptions());
        loggerFactory = MockHelpers.MakeMemoryLoggerFactory(logList, logStore);
        logger = loggerFactory.CreateReLogger(categoryName);
    }
}