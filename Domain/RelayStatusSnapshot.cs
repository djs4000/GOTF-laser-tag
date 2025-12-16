using System;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Describes the current relay configuration state and the most recent transmission attempt.
/// </summary>
public sealed record RelayStatusSnapshot(
    bool Enabled,
    bool IsSending,
    DateTimeOffset? LastAttemptUtc,
    bool? LastAttemptSucceeded,
    int? LastStatusCode,
    string? LastErrorMessage)
{
    public static RelayStatusSnapshot Disabled => new(false, false, null, null, null, null);

    public bool HasRecentActivity(TimeSpan freshnessWindow)
    {
        return LastAttemptUtc is { } timestamp && DateTimeOffset.UtcNow - timestamp <= freshnessWindow;
    }
}

public sealed class RelayStatusSnapshotEventArgs : EventArgs
{
    public RelayStatusSnapshotEventArgs(RelayStatusSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public RelayStatusSnapshot Snapshot { get; }
}
