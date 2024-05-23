using Imazen.Abstractions.Logging;
using Xunit.Abstractions;

namespace Imazen.Routing.Caching.Health.Tests;

public class TestLoggerAdapter : ITestLogging
{
    private readonly ITestOutputHelper _output;

    public TestLoggerAdapter(ITestOutputHelper output)
    {
        _output = output;
    }

    public void WriteLine(string message)
    {
        _output.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        _output.WriteLine(format, args);
    }
}