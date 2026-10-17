using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Captures externally owned per-node observer instances once before any application is built.</summary>
internal sealed class ControlledPartitionMovementNativeObservers(
    Func<int, IGrainPartitionMovementSealedOperationObserver>? observerFactory,
    Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory,
    Func<int, IGrainRequestPhaseObserver>? phaseObserverFactory,
    IGrainRequestPurposeObserver? purposeObserver = null)
{
    private const int SourceIndex = 0;
    internal IGrainRequestPurposeObserver? Purpose { get; } = purposeObserver;
    private IGrainPartitionMovementSealedOperationObserver[]? sealedObservers;
    private IDatabaseAuthenticationExecutionObserver[]? authenticationObservers;
    private IGrainRequestPhaseObserver[]? phaseObservers;

    internal IGrainPartitionMovementSealedOperationObserver? Sealed(int index) => sealedObservers?[index];
    internal IDatabaseAuthenticationExecutionObserver? Authentication(int index) => authenticationObservers?[index];
    internal IGrainRequestPhaseObserver? Phase(int index) => phaseObservers?[index];

    internal void Prepare()
    {
        if (observerFactory is not null && sealedObservers is null)
        {
            sealedObservers = new IGrainPartitionMovementSealedOperationObserver[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            for (var index = SourceIndex; index < sealedObservers.Length; index++)
            {
                sealedObservers[index] = observerFactory(index) ?? throw new InvalidOperationException(nameof(observerFactory));
            }
        }
        if (authenticationObserverFactory is not null && authenticationObservers is null)
        {
            authenticationObservers = new IDatabaseAuthenticationExecutionObserver[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            for (var index = SourceIndex; index < authenticationObservers.Length; index++)
            {
                authenticationObservers[index] = authenticationObserverFactory(index) ?? throw new InvalidOperationException(nameof(authenticationObserverFactory));
            }
        }
        if (phaseObserverFactory is not null && phaseObservers is null)
        {
            phaseObservers = new IGrainRequestPhaseObserver[ControlledPartitionMovementNativeHostEndpoints.OwnerCount];
            for (var index = SourceIndex; index < phaseObservers.Length; index++)
            {
                phaseObservers[index] = phaseObserverFactory(index) ?? throw new InvalidOperationException(nameof(phaseObserverFactory));
            }
        }
    }
}
