using System.Diagnostics;
using Imazen.Common.Licensing;

namespace Imazen.Common.Tests.Licensing;

public class FakeClock(string date, string buildDate) : ILicenseClock
{
    private DateTimeOffset now = DateTimeOffset.Parse(date);
    private readonly DateTimeOffset built = DateTimeOffset.Parse(buildDate);

    public void AdvanceSeconds(long seconds) { now = now.AddSeconds(seconds); }
    public DateTimeOffset GetUtcNow() => now;
    public long GetTimestampTicks() => now.Ticks;
    public long TicksPerSecond { get; } = Stopwatch.Frequency;
    public DateTimeOffset? GetBuildDate() => built;
    public DateTimeOffset? GetAssemblyWriteDate() => built;
}