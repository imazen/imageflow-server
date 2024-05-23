namespace Imazen.Abstractions.Logging;

public interface ITestLogging
{ 
    void WriteLine(string message);
    void WriteLine(string format, params object[] args);
}

