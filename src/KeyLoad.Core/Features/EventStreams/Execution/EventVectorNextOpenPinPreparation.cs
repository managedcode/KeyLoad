using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidNextEventVectorPin = "The original event vector opening Pin requires recovery.";

    internal EventVectorSourcePhase PrepareVerifiedNextEventVectorOpenPin(string principalId,
        EventVectorControlPhase originalOpen, int ordinal, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalOpen);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            var actual = RequireEventVectorControlView(bounded, principal, originalOpen);
            if (originalOpen.OriginalParentExpiresAt <= EvaluationClock.GetUtcNow())
            { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidNextEventVectorPin); }
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            var inventory = EventVectorMapInventory.Read(bounded, originalOpen.OriginalRequest.ControlPartition,
                originalOpen.OriginalRequest.MapId, OperationLimitsOptions, cancellationToken);
            var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidNextEventVectorPin);
            var entries = EventVectorEntryEncoding.Decode(originalOpen.OriginalEncodedEntries,
                originalOpen.OriginalEntryChecksum, admission);
            EventVectorOpenPinPrefix.Require(map, inventory, originalOpen, entries, ordinal, admission);
            var witness = EventVectorCoverageWitnessEncoding.Read(originalOpen.OriginalCoverageWitness,
                originalOpen.OriginalRequest, admission);
            using var decoded = new EventVectorFirstOpenDecodedOwner(witness);
            var entry = entries[ordinal];
            var sourceOwner = EventVectorOriginalEntryOwner.Require(witness, entry, admission, work);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidNextEventVectorPin);
            var controlOwner = ResolveRegisteredPlacementOwner(bounded, catalog.DefaultShard,
                actual.PhysicalShardId, ErrorCode.OwnershipLost);
            var mutation = EventVectorOpenPinMutation.Create(map, entry, sourceOwner, controlOwner,
                originalOpen.OriginalParentExpiresAt);
            cancellationToken.ThrowIfCancellationRequested();
            work.Check();
            work.ChargeBytes(NativeSerialization.Measure(mutation));
            work.ChargeBytes(SHA256.HashSizeInBytes);
            return EventVectorPhasePreparation.Prepare(map, ordinal, entry, mutation, admission);
        });
    }
}
