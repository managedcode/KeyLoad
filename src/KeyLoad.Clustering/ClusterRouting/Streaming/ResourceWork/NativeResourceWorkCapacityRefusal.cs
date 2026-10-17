using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Refuses a D4 acknowledgement without throwing capacity failure through a committed D3 apply.</summary>
internal sealed class NativeResourceWorkCapacityRefusal : IResourceLifetimeWorkSettlement
{
    private readonly Lazy<Task> original = new(() => Task.FromException(
        Errors.Fail(ErrorCode.BudgetExceeded, NativeRequestWorkLimits.CapacityMessage)));

    public Task CancelAndJoinAsync() => original.Value;
}
