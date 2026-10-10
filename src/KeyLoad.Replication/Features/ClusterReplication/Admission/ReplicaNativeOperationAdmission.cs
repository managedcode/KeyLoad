using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Replication;

// Synchronous borrowed admission only. Never return proxy strings as executable operations.
internal static class ReplicaNativeOperationAdmission
{
    private const int NoEntryPayloads = 0;

    internal const string SenderMismatch = "The replica command identity does not match its authenticated sender.";
    private const string InvalidOperation = "The native replica operation is invalid or unauthenticated.";

    internal static void Warmup() => ReplicaNativeInspection.Warmup<NativeCommandPayload>();

    internal static bool Validate<T>(ReplicaInspectedValue<T> inspected, ReplicatedOperation operation,
        DatabaseEngine? canonicalDatabase, int maximumControlPayloadBytes)
    {
        var database = canonicalDatabase ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaOperationAuthority.Required);
        if (operation.Id == Guid.Empty || !Enum.IsDefined(operation.Kind) || operation.NativePayload.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOperation);
        }
        var principal = Identity(inspected, operation.PrincipalId);
        var wrapper = ReplicaNativeInspection.InspectNative<NativeCommandPayload>(operation.NativePayload, maximumEntries: NoEntryPayloads);
        var value = wrapper.Value;
        var detail = Detail(wrapper, value.SafeDetail);
        database.VerifyNativeAuthority(operation.Id, operation.Kind, principal, inspected.Utf8(operation.PayloadJson).Span,
            value.Value, value.Error, detail, value.Authority, value.Signature, value.RetryDecisions, value.TransferProof);
        if (value.Error is null)
        {
            var type = DatabaseEngine.NativeOperationPayloadType(operation.Kind)
                ?? throw Errors.Fail(ErrorCode.Corruption, InvalidOperation);
            NativeSerialization.Validate(value.Value.Span, type);
        }
        var control = RuntimeJournalBootstrapAdmission.IsControl(operation, value,
            inspected.Utf8Length(operation.PayloadJson), maximumControlPayloadBytes);
        if (control && (inspected.Utf8Length(operation.PayloadJson) > maximumControlPayloadBytes
            || value.Value.Length > maximumControlPayloadBytes))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.PayloadExceeded);
        }
        return control;
    }

    private static string Identity<T>(ReplicaInspectedValue<T> inspected, string proxy)
    {
        if (proxy is null || inspected.Utf16Length(proxy) > ReplicaProtocol.MaximumIdentityCharacters)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOperation);
        }
        var value = inspected.Metadata(proxy, ReplicaProtocol.PayloadMetadataBytes);
        return !string.IsNullOrWhiteSpace(value) ? value : throw Errors.Fail(ErrorCode.Corruption, InvalidOperation);
    }

    private static string? Detail(ReplicaInspectedValue<NativeCommandPayload> inspected, string? proxy)
    {
        if (proxy is null)
        {
            return null;
        }
        if (inspected.Utf16Length(proxy) > ReplicaProtocol.MaximumDetailCharacters)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidOperation);
        }
        return inspected.Metadata(proxy, ReplicaProtocol.PayloadMetadataBytes);
    }
}
