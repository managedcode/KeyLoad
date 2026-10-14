using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutReader(global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> executionOptions, CancellationToken token)
{
    private const int ExaminedRecordStep = 1;
    private const int MinimumReadBytes = 0;
    private readonly DatabaseLimits limits = executionOptions.Value;
    private readonly List<NativeCanonicalModelCutRow> rows = [];
    private int examined;
    private long readBytes;
    private long copiedBytes;

    internal NativeCanonicalModelCutWitness Read(IKeyValueView view, NativeCanonicalModelCutRequest request,
        ReplicaSiloDiscovery actualOwner, long position)
    {
        ReadPartition(view, request.SourcePartition);
        ReadPartition(view, request.DestinationPartition);
        var logical = Outcome(view, request.LogicalPrincipalId, request);
        var technical = Outcome(view, request.TechnicalPrincipalId, request);
        var receipt = CopyValue(view, RemoteTransferStorage.TargetReceiptKey(request.SourceQueue,
            request.TransferId, request.DestinationQueue));
        var target = CopyValue(view, RemoteTransferStorage.TargetCapacityKey(request.DestinationQueue));
        var intent = CopyValue(view, RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId));
        var source = CopyValue(view, RemoteTransferStorage.SourceCapacityKey(request.SourceQueue));
        token.ThrowIfCancellationRequested();
        return new(request, actualOwner, position, rows.ToImmutableArray(), logical, technical, receipt, target, intent, source);
    }

    private void ReadPartition(IKeyValueView view, PartitionRef partition)
    {
        foreach (var family in PartitionRecordFamilies.All)
        {
            token.ThrowIfCancellationRequested();
            var result = view.VisitRange(KeySpace.Partition(family, partition), limits.MaxScanRecords,
                CopyRow, observer: Charge, cancellationToken: token);
            if (result.HasMore || result.StoppedByVisitor)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
        }
    }

    private bool CopyRow(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        RequireCopy(checked(key.Length + value.Length));
        rows.Add(new(key.ToArray(), value.ToArray()));
        return true;
    }

    private ReadOnlyMemory<byte>? Outcome(IKeyValueView view, string principal, NativeCanonicalModelCutRequest request)
    {
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.DestinationPartition);
        var bytes = CopyValue(view, CommandOutcomeKeyResolver.ForNew(principal, request.AcceptCommandId, scope).Key);
        var selected = CommandOutcomeKeyResolver.Select(view, principal, request.AcceptCommandId, scope, Charge);
        if ((selected.Outcome is null) != (bytes is null))
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        return bytes;
    }

    private ReadOnlyMemory<byte>? CopyValue(IKeyValueView view, byte[] key)
    {
        ReadOnlyMemory<byte>? result = null;
        view.ReadValue(key, value =>
        {
            RequireCopy(value.Length);
            result = value.ToArray();
        }, Charge);
        return result;
    }

    private void Charge(long actual)
    {
        token.ThrowIfCancellationRequested();
        examined = checked(examined + ExaminedRecordStep);
        readBytes = checked(readBytes + actual);
        if (actual < MinimumReadBytes || examined > limits.MaxScanRecords || readBytes > limits.MaxQueryReadBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
    }

    private void RequireCopy(int bytes)
    {
        token.ThrowIfCancellationRequested();
        copiedBytes = checked(copiedBytes + bytes);
        if (copiedBytes > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
    }
}
