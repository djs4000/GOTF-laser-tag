using System.Threading;
using System.Threading.Tasks;
using LaserTag.Defusal.Domain;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Exposes the relay functionality used by the coordinator so it can be stubbed in tests.
/// </summary>
public interface IRelayService
{
    bool IsEnabled { get; }

    Task TryRelayCombinedAsync(CombinedRelayPayload payload, CancellationToken cancellationToken);

    Task<RelaySendResult> RelayWithResponseAsync(CombinedRelayPayload payload, CancellationToken cancellationToken);
}

public sealed record RelaySendResult(bool Success, int? StatusCode, string? ErrorMessage);
