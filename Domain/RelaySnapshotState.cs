using System;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Describes the relay payload cache exposed to the Relay Monitor UI.
/// </summary>
public sealed record RelaySnapshotState(
    CombinedRelayPayload? OutboundPayload,
    DateTimeOffset? LastUpdatedUtc,
    bool IsStale,
    MatchSnapshotDto? LastInboundMatch,
    PropStatusDto? LastInboundProp)
{
    public static RelaySnapshotState Empty => new(null, null, true, null, null);
}

public sealed class RelaySnapshotEventArgs : EventArgs
{
    public RelaySnapshotEventArgs(RelaySnapshotState snapshot)
    {
        Snapshot = snapshot;
    }

    public RelaySnapshotState Snapshot { get; }
}
