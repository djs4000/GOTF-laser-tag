using System.Threading;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Abstraction over the UI automation that terminates the match in the LT software.
/// </summary>
public interface IFocusService
{
    void BindToUiThread(SynchronizationContext context);

    Task<FocusActionResult> TryEndMatchAsync(string reason, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a focus attempt.
/// </summary>
/// <param name="FocusAcquired">Indicates whether the target window was foregrounded.</param>
/// <param name="Description">Human readable summary for UI display.</param>
public readonly record struct FocusActionResult(bool FocusAcquired, string Description);
