using KeyLoad.Features.InternalSerialization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireOriginalCommandContent(Command<DatabaseCommand> operation, StoredOutcome outcome)
    {
        var original = outcome.PreparedCommand;
        var input = operation.Value!;
        if (original.Value is not { } admitted || admitted.Fingerprint != outcome.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidDatabaseCommand);
        }
        if (original.CommandId != operation.CommandId || original.UserId != operation.UserId
            || admitted.Kind != input.Kind
            || input.Fingerprint != outcome.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
        var type = NativeOperationPayloadType(input.Kind)
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand);
        var requested = NativeSerialization.Deserialize(input.Input.Span, type, NativeValidationProfile.PublicInputElements);
        var retained = NativeSerialization.Deserialize(admitted.Input.Span, type, NativeValidationProfile.PublicInputElements);
        if (JsonData.Fingerprint(requested) != JsonData.Fingerprint(retained))
        {
            throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
        }
    }
}
