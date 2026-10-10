using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorCaptureVersion = 1;
    private const int EventVectorFirstGroup = 0;
    private const long EventVectorInitialAppliedCut = 0;
    private const string InvalidEventVectorCapture = "The authorized native event vector coverage request is inconsistent.";

    internal EventVectorCoverageCapture CaptureEventVectorCoverage(string principalId,
        EventFeedControlRequest request, int groupOrdinal, PhysicalShardRecord sourceOwner,
        PhysicalShardRecord controlOwner, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceOwner);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        return Store.Read(view => CaptureEventVectorCoverage(view, principalId, request, groupOrdinal,
            sourceOwner, controlOwner, work, cancellationToken));
    }

    private EventVectorCoverageCapture CaptureEventVectorCoverage(IKeyValueView view, string principalId,
        EventFeedControlRequest request, int groupOrdinal, PhysicalShardRecord sourceOwner,
        PhysicalShardRecord controlOwner, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
        RequireEventVectorCaptureRequest(request, groupOrdinal);
        var inventory = new EventVectorInventoryReadBudget(OperationLimitsOptions);
        var catalog = EventVectorCoverageCatalogRead.Read(bounded, sourceOwner, controlOwner, inventory);
        if (sourceOwner.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorCapture); }
        var applied = EventVectorInitialAppliedCut;
        bounded.ReadValue(KeySpace.AppliedBytes, bytes => applied = NativeSerialization.Deserialize<long>(bytes),
            inventory.Observe);
        var identity = Store.Identity;
        var storeCut = Store.Position;
        var roster = EventVectorRosterRead.Read(bounded, request.ControlPartition, request.Scope.Domain,
            identity.Incarnation, storeCut, applied, OperationLimitsOptions, cancellationToken);
        var directory = EventVectorCoverageRows.Optional(bounded,
            AtomicPartitionPlacementSerialization.DirectoryKey(), inventory);
        if (directory is not null)
        {
            AtomicPartitionPlacementValidation.ValidateDirectory(
            AtomicPartitionPlacementSerialization.ReadDirectory(bounded));
        }
        var entries = ImmutableArray.CreateBuilder<EventVectorEntry>();
        var placements = ImmutableArray.CreateBuilder<EventVectorCoverageRow>();
        var heads = ImmutableArray.CreateBuilder<EventVectorCoverageRow>();
        CaptureEventVectorRoster(bounded, principal, request, sourceOwner, catalog.Catalog.DefaultShard,
            roster, work, inventory, applied, entries, placements, heads, cancellationToken);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var encoded = EventVectorEntryEncoding.Encode(entries.ToImmutable(), admission);
        var result = EventVectorCaptureResult(request, groupOrdinal, sourceOwner, principal, identity,
            storeCut, applied, catalog, roster, directory, placements, heads, encoded);
        admission.RequireEncodedBytes(NativeSerialization.Measure(result));
        work.CheckResult(result);
        return result;
    }

    private static void RequireEventVectorCaptureRequest(EventFeedControlRequest request, int groupOrdinal)
    {
        if (request.Version != EventVectorCaptureVersion || request.CommandId == Guid.Empty
            || request.MapId == Guid.Empty || request.ControlPartition is null || request.Scope is null
            || groupOrdinal < EventVectorFirstGroup || !Enum.IsDefined(request.Start)
            || request.Scope.Kind is not (EventSourceKind.Topic or EventSourceKind.Stream))
        { throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorCapture); }
        ValidatePartition(request.ControlPartition);
        JsonData.Identifier(request.Scope.Domain);
        JsonData.Identifier(request.Scope.Resource);
    }
}
