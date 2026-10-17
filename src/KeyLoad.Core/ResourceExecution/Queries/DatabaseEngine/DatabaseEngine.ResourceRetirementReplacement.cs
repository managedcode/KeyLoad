using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Requires no live retirement before replacement under the native owner's existing exclusive apply lifetime.</summary>
    internal void RequireNoRetirementForSnapshotReplacement(ReadExecutionBudget work)
        => ResourceRetirementSnapshotReplacement.Require(Store, Limits, work);
}
