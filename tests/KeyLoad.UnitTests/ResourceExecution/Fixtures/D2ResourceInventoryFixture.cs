using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class D2ResourceInventoryFixture : IDisposable
{
    internal ControlledPartitionMovementNode Owner { get; } = new(D2ResourceInventoryData.Control.Owner);
    internal DatabaseEngine Database => Owner.Database;

    internal ReadExecutionBudget Work(CancellationToken token)
        => new(UnitExecutionOptions.DatabaseLimits(), Database.EvaluationClock, token);

    internal ReplicatedOperation Operation<T>(OperationKind kind, T request, string principal = D2ResourceInventoryData.Root)
        => Database.CreateNativeOperation(kind, Guid.NewGuid(), principal,
            Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));

    internal OperationResult Submit(ReplicatedOperation operation, CancellationToken token)
    {
        var result = Owner.Journal.Submit(operation, token);
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The genuine D2 fixture command failed: " + result.Error);
        }
        return result;
    }

    internal ResourceOwnerInventoryControlV1 Capture(ResourceLifetimeV1 lifetime, ReadExecutionBudget work)
    {
        var request = Database.AdmitResourceOwnerInventoryRequest(D2ResourceInventoryData.Administrator, Guid.NewGuid(),
            D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, lifetime.ResourceName,
            D2ResourceInventoryData.PageSize,
            Database.EvaluationClock.GetUtcNow().AddSeconds(Database.Limits.QueryDeadlineSeconds), work);
        return Database.CaptureResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator, request, work);
    }

    internal ResourceOwnerInventoryPageV1 Page(ResourceOwnerInventoryControlV1 control,
        RegisteredPhysicalOwnerV1 observed, ResourceOwnerInventoryCursorV1? cursor, ReadExecutionBudget work)
        => Database.ReadResourceOwnerInventoryPage(D2ResourceInventoryData.Administrator, control, observed, cursor, work);

    public void Dispose() => Owner.Dispose();
}
