using KeyLoad.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionIdentity operations against the caller's database context.</summary>
public static class CommandExecutionIdentity
{
    internal const string DatabaseCommandType = nameof(DatabaseCommand);
    internal const string InvalidDatabaseCommand = "The database command identity, subject, time or input is invalid.";
    internal const int FingerprintCharacters = 64;
    internal const int SnapshotStart = 0;
    /// <summary>Snapshots a trusted execution command before asynchronous admission or retained use.</summary>
    /// <param name = "operation">Execution command whose subject was bound by the server boundary.</param>
    /// <returns>An independently owned command and generated execution input.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Command<DatabaseCommand> OwnCommand(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return SnapshotCommand(operation, engine.Limits);
    }

    internal static Command<DatabaseCommand> SnapshotCommand(Command<DatabaseCommand> operation, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateDatabaseCommand(operation, limits);
        using var buffer = new DatabaseCommandSnapshotBuffer.CommandSnapshotBuffer(limits.MaxBatchBytes);
        OrleansSerialization.Serialize(operation, buffer);
        var owned = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(buffer.GetBuffer().AsSpan(SnapshotStart, checked((int)buffer.Length)));
        ValidateDatabaseCommand(owned, limits);
        return owned;
    }

    internal static void ValidateDatabaseCommand(Command<DatabaseCommand> operation, DatabaseLimits limits)
    {
        var input = operation.Value;
        if (operation.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(operation.UserId) || operation.CommandType != DatabaseCommandType || operation.Timestamp.Kind != DateTimeKind.Utc || input is null || input.Input.IsEmpty || !Enum.IsDefined(input.Kind) || string.IsNullOrEmpty(input.Fingerprint) || input.Fingerprint.Length != FingerprintCharacters || input.Fingerprint.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDatabaseCommand);
        }

        RequireBudget(checked((long)input.Input.Length + input.RetryDecisions.Length + input.ResourceAdmission.Length), limits.MaxBatchBytes);
        _ = OrleansSerialization.Deserialize(input.Input.Span, DatabaseCommandInputTypes.OperationPayloadType(input.Kind) ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand), ValidationProfile.PublicInputElements);
    }

    internal static Command<DatabaseCommand> CreatePreparedCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> input, string fingerprint, ReadExecutionBudget? work, IKeyValueView? admissionView = null)
    {
        var operation = Command<DatabaseCommand>.Create(DatabaseCommandType, new DatabaseCommand(kind, input, fingerprint), id);
        operation.UserId = principalId;
        operation.Timestamp = evaluatedAt.UtcDateTime;
        byte[] admission;
        if (admissionView is not null)
        {
            if (ResourceEffectCompositionScopes.RequiresCompositionEffectCapture(operation))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand);
            }

            work?.Check();
            admission = engine.EncodeResourceEffectAdmission(engine.CaptureResourceEffectAdmission(admissionView, operation, fingerprint, preserveBudgetFailure: work is not null));
            work?.Check();
        }
        else
        {
            admission = work is null ? engine.CaptureResourceEffectAdmission(operation) : engine.CaptureResourceEffectAdmission(operation, work);
        }

        operation.Value = operation.Value! with
        {
            ResourceAdmission = admission
        };
        return engine.OwnCommand(operation);
    }

    internal static Command<DatabaseCommand> EvaluateCommand(this DatabaseEngine engine, Command<DatabaseCommand> operation, DateTimeOffset evaluatedAt)
    {
        var evaluated = engine.OwnCommand(operation);
        evaluated.Timestamp = evaluatedAt.UtcDateTime;
        return evaluated;
    }

    internal static void RequireBudget(this DatabaseEngine engine, long byteCount) => RequireBudget(byteCount, engine.Limits.MaxBatchBytes);
    internal static void RequireBudget(long byteCount, int maximumBytes)
    {
        if (byteCount > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AtomicCommandCommit.InvalidCommandBudgetMessage);
        }
    }
}
