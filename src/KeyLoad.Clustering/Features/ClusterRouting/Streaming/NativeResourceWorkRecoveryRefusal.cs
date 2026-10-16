using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>An old committed fence cannot reconstruct the work that belonged to an earlier process.</summary>
internal sealed class NativeResourceWorkRecoveryRefusal : IResourceLifetimeWorkSettlement
{
    private readonly Lazy<Task> original = new(() => Task.FromException(
        Errors.Fail(ErrorCode.RecoveryRequired, NativeRequestWorkLimits.LeaseIdentityMessage)));

    public Task CancelAndJoinAsync() => original.Value;
}
