namespace Imazen.Abstractions.Logging;

/// <summary>
/// Identically behaved to xunit ITestOutputHelper, but without the dependency on xunit
/// </summary>
public interface ITestLogging
{ 
    void WriteLine(string message);
    /// <summary>
    /// Format strings can only contain {0}, {1}, {2}, etc. 
    /// </summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    void WriteLine(string format, params object[] args);
}

