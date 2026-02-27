using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Imazen.Common.Licensing
{
    class RealClock : ILicenseClock
    {
        public long TicksPerSecond { get; } = Stopwatch.Frequency;

        public long GetTimestampTicks() => Stopwatch.GetTimestamp();

        public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

        public DateTimeOffset? GetBuildDate()
        {
            try {
                return GetType()
                    .Assembly.GetCustomAttributes(typeof(BuildDateAttribute), false)
                    .Select(a => ((BuildDateAttribute) a).ValueDate)
                    .FirstOrDefault();
            } catch {
                return null;
            }
        }

        public DateTimeOffset? GetAssemblyWriteDate()
        {
            try {
#pragma warning disable IL3000 // Assembly.Location returns empty string in single-file/AOT
                var path = GetType().Assembly.Location;
#pragma warning restore IL3000
                return !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? new DateTimeOffset?(File.GetLastWriteTimeUtc(path))
                    : null;
            } catch {
                return null;
            }
        }
    }
}
