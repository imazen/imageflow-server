namespace Imazen.Abstractions.Blobs;

/// <summary>
/// Should be unique to a specific remote server or folder on a server.
/// For example, each container or bucket should provide a unique tracking zone.
/// Each mapped folder for local or network files should provide a unique tracking zone.
/// Each remote server should provide a unique tracking zone.
/// Cannot always be prefix-specific, for example with remote reader (encoded uri to multiple servers, shared prefix)
/// This should not differ more than required, otherwise the self-tuning
/// capabilities of the cache logic will be degraded.
/// </summary>
/// <param name="TrackingZone"></param>
public record LatencyTrackingZone(
    string TrackingZone, int DefaultMs, bool AlwaysShield = false);