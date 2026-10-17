using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorOriginalPinOutcome
{
    private const string InvalidOutcome = "The original native event vector Pin outcome is inconsistent.";

    internal static (StoredOutcome? Outcome, ReadOnlyMemory<byte> Digest) Read(IKeyValueView view,
        EventVectorSourceMutation release, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(admission);
        var key = KeySpace.PartitionOutcome(release.Source.Partition, release.PrincipalId,
            release.OriginalPinCommandId);
        EventVectorRowStorage.Read<StoredOutcome>(view, key, admission);
        var selection = CommandOutcomeKeyResolver.Select(view, release.PrincipalId,
            release.OriginalPinCommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, release.Source.Partition));
        if (selection.Outcome is not { } outcome)
        {
            return (null, ReadOnlyMemory<byte>.Empty);
        }
        var original = NativeSerialization.Deserialize<EventVectorSourceMutation>(release.OriginalPinNativeBody.Span);
        var json = JsonSerializer.SerializeToUtf8Bytes(original, JsonDefaults.Options);
        var fingerprint = NativeOperationFingerprint.Compute(release.OriginalPinCommandId,
            OperationKind.EventFeedSourcePhase, release.PrincipalId, json);
        if (outcome.Fingerprint != fingerprint || outcome.Incarnation != original.SourceOwner.Incarnation
            || outcome.PolicyEpoch != original.SourcePolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome);
        }
        var digest = ReadOnlyMemory<byte>.Empty;
        var found = view.ReadValue(selection.Key, bytes =>
        {
            admission.RequireEncodedBytes(checked(selection.Key.LongLength + bytes.Length));
            digest = SHA256.HashData(bytes);
        });
        if (!found)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOutcome);
        }
        return (outcome, digest);
    }
}
