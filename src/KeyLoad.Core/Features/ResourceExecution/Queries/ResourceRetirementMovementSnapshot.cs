using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Captures actual accepted physical work from the original gated view; no resource provenance is inferred.</summary>
internal sealed class ResourceRetirementMovementSnapshot(IKeyValueView view, IOptions<DatabaseLimits> options)
{
    private readonly DatabaseLimits limits = options.Value;
    private readonly HashSet<(PartitionRef Partition, Guid Move)> accepted = [];
    private readonly Dictionary<(PartitionRef Partition, Guid Move), string> retired = [];
    private readonly ResourceRetirementMetadataScan scan = new(view, options);

    internal static ImmutableArray<ResourceRetirementAcceptedMoveV1> Capture(IKeyValueView view, IOptions<DatabaseLimits> options)
    {
        var capture = new ResourceRetirementMovementSnapshot(view, options);
        capture.scan.Visit(KeyCodec.Encode(PartitionMoveProtocol.CleanupSpace), capture.Cleanup);
        ResourceRetirementParentSnapshot.Capture(capture.scan, capture.Add);
        capture.scan.Visit(KeyCodec.Encode(PartitionMoveProtocol.RecordSpace), capture.Control);
        capture.scan.Visit(KeyCodec.Encode(PartitionMoveProtocol.TargetStageSpace), capture.Target);
        capture.scan.Visit(KeyCodec.Encode(PartitionMoveProtocol.SourceFenceSpace), capture.Fence);
        capture.scan.Visit(KeyCodec.Encode(ResourceOwnerInventoryMovementProtocol.ImportSpace), capture.Import);
        var result = capture.accepted.OrderBy(item => item.Partition.TenantId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(item => item.Partition.PartitionKey, StringComparer.Ordinal).ThenBy(item => item.Move)
            .Select(item => new ResourceRetirementAcceptedMoveV1(item.Partition, item.Move)).ToImmutableArray();
        ResourceRetirementPreparationValidation.Moves(result, capture.limits);
        return result;
    }

    private void Add(PartitionRef partition, Guid move)
    {
        if (partition is null || move == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        DatabaseEngine.ValidatePartition(partition);
        accepted.Add((partition, move));
        if (accepted.Count > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
    }

    private bool Control(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var record = NativeSerialization.Deserialize<PartitionMoveControlRecord>(value);
        if (record?.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        PartitionMoveControlValidation.Require(record, record.Partition);
        ResourceRetirementMetadataScan.Exact(key, KeySpace.Partition(PartitionMoveProtocol.RecordSpace, record.Partition, record.MoveId));
        if (record.Phase is not (PartitionMovePhase.Retired or PartitionMovePhase.Aborted))
        {
            Add(record.Partition, record.MoveId);
        }
        return true;
    }

    private bool Target(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var stage = NativeSerialization.Deserialize<PartitionMoveTargetStage>(value);
        if (stage is null || stage.Version != PartitionMoveProtocol.Version || stage.Control?.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        PartitionMoveControlValidation.Require(stage.Control, stage.Control.Partition);
        ResourceRetirementMetadataScan.Exact(key, PartitionMoveTargetStorage.Key(stage.Control.Partition));
        if (!stage.Published)
        {
            Add(stage.Control.Partition, stage.Control.MoveId);
        }
        return true;
    }

    private bool Cleanup(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var state = ResourceRetirementMovementSnapshotValidation.Cleanup(key, value);
        if (state.Completion is null)
        {
            Add(state.Partition, state.MoveId);
        }
        else if (state.Role == PartitionMoveCleanupRole.Source && state.Stage == PartitionMovePeerStage.Retire)
        {
            retired.Add((state.Partition, state.MoveId), state.ControlIntentDigest);
        }
        return true;
    }

    private bool Fence(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var record = NativeSerialization.Deserialize<PartitionMoveSourceFenceRecord>(value);
        if (record?.Partition is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        PartitionMoveSourceFenceValidation.Require(record, record.Partition);
        ResourceRetirementMetadataScan.Exact(key, PartitionMoveSourceFenceStorage.Key(record.Partition));
        if (!retired.TryGetValue((record.Partition, record.MoveId), out var digest) || digest != record.ControlIntentDigest)
        {
            Add(record.Partition, record.MoveId);
        }
        return true;
    }

    private bool Import(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var record = NativeSerialization.Deserialize<ResourceOwnerInventoryImportOriginV1>(value);
        if (record is null || record.Version != ResourceOwnerInventoryMovementProtocol.Version || record.Partition is null
            || record.MoveId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementMetadataScan.Exact(key, ResourceOwnerInventoryMovementRows.ImportKey(record.Partition, record.MoveId));
        Add(record.Partition, record.MoveId);
        return true;
    }
}
