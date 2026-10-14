using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedPlacementIntentPersistence(Guid commandId, string principalId,
        PersistPlacementIntentRequest original, ImmutableArray<OwnerCapacitySnapshotV1> capacity, string? cohort)
    {
        if (commandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid);
        }
        var payload = NativeSerialization.Serialize(new PlacementIntentPersistenceAdmissionV1(original, capacity, cohort));
        RequireNativeBudget(payload.Length);
        var identity = System.Text.Json.JsonSerializer.Serialize(original, JsonDefaults.Options);
        return IssueNativeOperation(new(commandId, OperationKind.PersistPlacementIntent, principalId,
            EvaluationClock.GetUtcNow(), identity), new NativeCommandPayload(payload));
    }

    internal bool RequiresFreshPlacementPersistence(ReplicatedOperation prepared, ReadExecutionBudget work)
        => Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, prepared.PrincipalId, EvaluationClock.GetUtcNow());
            // Ordinary apply owns and retains denial; this probe never substitutes that failure or its ACK.
            if (!principal.ClusterAdministrator)
            {
                return false;
            }
            _ = AuthorizeOperation(charged, principal, prepared);
            var scope = CommandOutcomePartitionIdentity.Resolve(prepared);
            var retained = CommandOutcomeKeyResolver.Select(charged, prepared.PrincipalId, prepared.Id, scope);
            work.Check();
            return retained.Outcome is null;
        });
}
