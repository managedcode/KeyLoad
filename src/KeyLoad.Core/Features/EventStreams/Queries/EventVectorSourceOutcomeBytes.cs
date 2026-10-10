using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorSourceOutcomeBytes
{
    private const string InvalidOutcome = "The original event vector source outcome bytes are inconsistent.";

    internal static ReadOnlyMemory<byte> Read(IKeyValueView view, EventVectorSourcePhase phase,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        var mutation = EventVectorSourceMutationValidation.ReadOriginal(phase, admission);
        var key = KeySpace.PartitionOutcome(mutation.Source.Partition, mutation.PrincipalId, phase.PhaseCommandId);
        var row = EventVectorRowStorage.Read<StoredOutcome>(view, key, admission);
        var selected = CommandOutcomeKeyResolver.Select(view, mutation.PrincipalId, phase.PhaseCommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, mutation.Source.Partition));
        if (selected.Outcome is null)
        {
            if (row is not null)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome); }
            return ReadOnlyMemory<byte>.Empty;
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(mutation, JsonDefaults.Options);
        admission.RequireEncodedBytes(json.LongLength);
        var expected = NativeOperationFingerprint.Compute(phase.PhaseCommandId, OperationKind.EventFeedSourcePhase,
            mutation.PrincipalId, json);
        if (row is null || row.Fingerprint != expected || row.Incarnation != mutation.SourceOwner.Incarnation
            || row.PolicyEpoch != mutation.SourcePolicyEpoch)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome); }
        var original = ReadOnlyMemory<byte>.Empty;
        var found = view.ReadValue(selected.Key, bytes =>
        {
            admission.RequireEncodedBytes(checked(selected.Key.LongLength + bytes.Length));
            original = bytes.ToArray();
        });
        if (!found)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome); }
        return original;
    }
}
