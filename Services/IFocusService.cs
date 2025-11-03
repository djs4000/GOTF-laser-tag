using System.Threading;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Abstraction over the UI automation that terminates the match in the LT software.
/// </summary>
public interface IFocusService
{
    void BindToUiThread(SynchronizationContext context);

    Task<FocusActionResult> TryEndMatchAsync(string reason, CancellationToken cancellationToken);

    Task<FocusActionResult> TryFocusWindowAsync(string reason, CancellationToken cancellationToken);

    FocusWindowInfo GetForegroundWindowInfo();
}

/// <summary>
/// Outcome of a focus attempt.
/// </summary>
/// <param name="FocusAcquired">Indicates whether the target window was foregrounded.</param>
/// <param name="Description">Human readable summary for UI display.</param>
public readonly record struct FocusActionResult(bool FocusAcquired, string Description);

/// <summary>
/// Snapshot describing the current foreground window.
/// </summary>
/// <param name="Handle">Handle of the active foreground window.</param>
/// <param name="WindowTitle">Title text of the foreground window, if any.</param>
/// <param name="IsTargetForeground">True when the LT application is already focused.</param>
public readonly record struct FocusWindowInfo(IntPtr Handle, string? WindowTitle, bool IsTargetForeground)
{
    public static FocusWindowInfo Empty => new(IntPtr.Zero, null, false);
}
