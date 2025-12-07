using System;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Describes the relay payload cache exposed to the Relay Monitor UI.
/// </summary>
public sealed record RelaySnapshotState(
    CombinedRelayPayload? Payload,
    DateTimeOffset? LastUpdatedUtc,
    bool IsStale)
{
    public static RelaySnapshotState Empty => new(null, null, true);
}

public sealed class RelaySnapshotEventArgs : EventArgs
{
    public RelaySnapshotEventArgs(RelaySnapshotState snapshot)
    {
        Snapshot = snapshot;
    }

    public RelaySnapshotState Snapshot { get; }
}
