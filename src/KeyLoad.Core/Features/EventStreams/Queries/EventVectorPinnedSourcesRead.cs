using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int FirstPinnedReadOrdinal = 0;
    private const int EventVectorPinnedReadVersion = 1;
    private const string InvalidEventVectorPinnedRead = "The original event vector source read authority changed.";

    internal ImmutableArray<EventSourcePage> ReadEventVectorPinnedSources(string principalId,
        EventVectorSourcesReadRequest input, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(input.SourceOwner);
        work.Check();
        work.ConstrainResultBytes(checked((int)Math.Min(Limits.MaxBatchBytes, Limits.MaxQueryReadBytes)));
        if (input.Version != EventVectorPinnedReadVersion || input.Requests.IsDefault || input.OriginalPins.IsDefault
            || input.Requests.Length != input.OriginalPins.Length || input.Requests.Length > Limits.MaxResults
            || input.Requests.Any(static request => request is null || request.Source is null)
            || input.Requests.Select(static request => request.Source).Distinct().Count() != input.Requests.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorPinnedRead);
        }
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        admission.RequireEncodedBytes(NativeSerialization.Measure(input));
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            foreach (var request in input.Requests)
            {
                Authorization.Require(principal, request.Source.Partition, request.Source.Resource,
                    Capability.SubscriptionsManage);
                Authorization.Require(principal, request.Source.Partition, request.Source.Resource,
                    SourceReadCapability(request.Source));
            }
            RequireEventVectorReadPins(bounded, principal, input, work);
            return ReadEventVectorSources(view, principalId, input.Requests, input.SourceOwner, work);
        });
    }

    private void RequireEventVectorReadPins(IKeyValueView bounded, PrincipalRecord principal,
        EventVectorSourcesReadRequest input, ReadExecutionBudget work)
    {
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorPinnedRead);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        for (var ordinal = FirstPinnedReadOrdinal; ordinal < input.Requests.Length; ordinal++)
        {
            work.Check();
            var request = input.Requests[ordinal];
            var mutation = EventVectorSourceReadPin.Require(bounded, request, input.OriginalPins[ordinal],
                principal, admission);
            var placement = StreamTraversalPlacement(bounded, request.Source.Partition, input.SourceOwner);
            if (!PhysicalOwnerEntryValidation.SameOwner(mutation.SourceOwner, input.SourceOwner)
                || !PhysicalOwnerEntryValidation.SameOwner(mutation.ControlOwner, catalog.DefaultShard)
                || mutation.ControlIncarnation != catalog.DefaultShard.Incarnation
                || mutation.LogicalPlacementRevision != placement.Revision
                || mutation.DirectoryFence != placement.DirectoryRevision
                || SourceResource(bounded, request.Source).SchemaVersion != mutation.SourceSchemaVersion)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorPinnedRead);
            }
            _ = EventVectorStoredSourceHead(bounded, request.Source);
        }
    }
}
