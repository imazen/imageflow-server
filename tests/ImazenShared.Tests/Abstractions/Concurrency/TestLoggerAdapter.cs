using Imazen.Abstractions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Imazen.Routing.Caching.Health.Tests;

public class TestLoggerAdapter : ITestLogging, ITestOutputHelper
{
    private readonly ITestOutputHelper _output;
    private int _lineCount = 0;
    
    public int LineCount => _lineCount;

    public TestLoggerAdapter(ITestOutputHelper output)
    {
        _output = output;
    }

    public void WriteLine(string message)
    {
        Interlocked.Increment(ref _lineCount);
        _output.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        Interlocked.Increment(ref _lineCount);
        _output.WriteLine(format, args);
    }
}

public static class TestLoggerExtensions
{
    public static void AssertLog(this TestLoggerAdapter adapter, int line, string message)
    {
        
        Assert.True(adapter.LineCount == line, $"Expected {line} lines in log, but found {adapter.LineCount}");
        adapter.WriteLine(message);
    }
    
    public static void AssertLogCount(this TestLoggerAdapter adapter, int count)
    {
        Assert.Equal(count, adapter.LineCount);
    }
}