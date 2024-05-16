using System.Diagnostics;
using Imazen.Common.Licensing;

namespace Imazen.Common.Tests.Licensing;

/// <summary>
/// Time advances normally, but starting from the given date instead of now
/// </summary>
public class OffsetClock(string date, string buildDate) : ILicenseClock
{
    private TimeSpan offset = DateTimeOffset.UtcNow - DateTimeOffset.Parse(date);
    private readonly long ticksOffset = Stopwatch.GetTimestamp() - 1;
    private readonly DateTimeOffset built = DateTimeOffset.Parse(buildDate);

    public void AdvanceSeconds(int seconds) { offset += new TimeSpan(0,0, seconds); }
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow - offset;
    public long GetTimestampTicks() => Stopwatch.GetTimestamp() - ticksOffset;
    public long TicksPerSecond { get; } = Stopwatch.Frequency;
    public DateTimeOffset? GetBuildDate() => built;
    public DateTimeOffset? GetAssemblyWriteDate() => built;
}