using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using static KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentCapacityPrimitives;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original stopped raw data sizes only; none of these bounds becomes authority or a phase.</summary>
internal static class PartitionMovementRetainedPageCapacityRf3Bounds
{
    private const int ByteBoundaryStep = 1;
    private const int SingleOriginalPage = 1;
    private const char RowSeparator = ':';
    private const int FirstRowPart = 0;

    internal static async Task<int> RequireAsync(PartitionMoveParentState actual,
        PartitionMovementPublicParentRf3NativeCut[] cuts, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var header = actual.Header ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var capture = actual.Selected ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var control = actual.Control ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(actual.Pending).IsNull();
        await Assert.That(PhysicalOwnerEntryValidation.SameOwner(header.OriginalSourceOwner!, header.ControlOwner)).IsTrue();
        await Assert.That(capture.Stage).IsEqualTo(PartitionMovePeerStage.Capture);
        await Assert.That(capture.CaptureProofCheckpointReceipt).IsNotNull();
        await Assert.That(capture.ObservationCheckpointReceipt).IsNotNull();
        var descriptor = capture.OriginalDescriptor!;
        var family = descriptor.Families.Single(value => value.Family == PartitionRecordFamilies.BlobPart);
        var prefix = KeySpace.Partition(PartitionRecordFamilies.BlobPart, seed.Partition);
        var source = cuts.First(value => value.Header is not null);
        var records = source.Rows.Select(Parse).Where(value => value.Key.Span.StartsWith(prefix)).ToArray();
        await Assert.That((long)records.Length).IsEqualTo(family.RecordCount);
        await Assert.That(records.Sum(static value => (long)value.Key.Length + value.Value.Length)).IsEqualTo(family.RawBytes);
        await Assert.That(PartitionMoveImageDigest.Records(records)).IsEqualTo(family.Digest);
        await Assert.That(family.PageCount).IsEqualTo(SingleOriginalPage);
        var cap = checked((int)(family.RawBytes - ByteBoundaryStep));
        var limits = IntegrationExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = cap }).Value;
        limits.Validate();
        await Assert.That(records.Length).IsLessThanOrEqualTo(limits.MaxBatchMutations);
        PartitionMoveDescriptorValidation.Require(descriptor, capture.OriginalFence!, limits);
        var original = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(capture.OriginalPhase!.Body.Span);
        await Assert.That(original.MaximumPageBytes).IsGreaterThan(cap);
        await Assert.That(original.MaximumImageBytes).IsLessThanOrEqualTo(limits.MaxQueryReadBytes);
        await RequireImageRemainingAsync(descriptor, source.Rows, original.MaximumImageBytes);
        await Assert.That(original.MaximumRecords).IsLessThanOrEqualTo(limits.MaxScanRecords);
        var metadata = PartitionMovementParentCapacityFuture.Root<PartitionMoveTransferReadAuthority>(checked(
            ReferenceFieldBytes + PartitionMovementParentCapacityBounds.Bound(header)
            + PartitionMovementParentCapacityBounds.Bound(capture)
            + PartitionMovementParentCapacityBounds.Bound(control) + ScalarFieldBytes));
        await Assert.That(metadata).IsLessThanOrEqualTo((long)cap);
        var envelope = PartitionMovementParentCheckpointOwner.OriginalEnvelope(capture, release: false);
        PartitionMovePeerEnvelopeValidation.RequireStructure(envelope, cap);
        return cap;
    }

    private static async Task RequireImageRemainingAsync(PartitionMoveImageDescriptor descriptor,
        IReadOnlyList<string> rows, long originalImageLimit)
    {
        long retainedUpperBound = PartitionMoveProtocol.EmptyCount;
        foreach (var family in descriptor.Families)
        {
            if (PartitionMoveFamilyCapture.ControlOwned(family.Family))
            { continue; }
            var prefix = KeySpace.Partition(family.Family, descriptor.Partition);
            var maximumKey = rows.Select(Parse).Where(value => value.Key.Span.StartsWith(prefix))
                .Select(static value => value.Key.Length).DefaultIfEmpty(PartitionMoveProtocol.EmptyCount).Max();
            var continuations = Math.Max(PartitionMoveProtocol.EmptyCount, family.PageCount - SingleOriginalPage);
            retainedUpperBound = checked(retainedUpperBound + family.RawBytes + (long)continuations * maximumKey);
        }
        await Assert.That(retainedUpperBound).IsLessThan(originalImageLimit);
    }

    private static KeyValueRecord Parse(string row)
    {
        var split = row.IndexOf(RowSeparator, StringComparison.Ordinal);
        if (split <= FirstRowPart)
        { throw new InvalidOperationException(PartitionMoveProtocol.InvalidImage); }
        return new(Convert.FromHexString(row.AsSpan(FirstRowPart, split)),
            Convert.FromHexString(row.AsSpan(split + ByteBoundaryStep)));
    }
}
