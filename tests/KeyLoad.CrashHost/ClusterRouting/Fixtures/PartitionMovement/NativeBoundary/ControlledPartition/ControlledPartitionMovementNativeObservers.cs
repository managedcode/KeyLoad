using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Captures externally owned per-node observer instances once before any application is built.</summary>
internal sealed class ControlledPartitionMovementNativeObservers(
    Func<int, IDatabaseAuthenticationExecutionObserver>? authenticationObserverFactory,
    Func<int, IGrainRequestPhaseObserver>? phaseObserverFactory,
    IGrainRequestPurposeObserver? purposeObserver = null)
{
    private const int SourceIndex = 0;
    internal IGrainRequestPurposeObserver? Purpose { get; } = purposeObserver;
    private IDatabaseAuthenticationExecutionObserver[]? authenticationObservers;
    private IGrainRequestPhaseObserver[]? phaseObservers;

    internal IDatabaseAuthenticationExecutionObserver? Authentication(int index) => authenticationObservers?[index];
    internal IGrainRequestPhaseObserver? Phase(int index) => phaseObservers?[index];

    internal void Prepare()
    {
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
