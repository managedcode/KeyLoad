using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string DatabaseCommandType = nameof(DatabaseCommand);
    private const string InvalidDatabaseCommand = "The database command identity, subject, time or input is invalid.";
    private const int FingerprintCharacters = 64;
    private const int SnapshotStart = 0;

    /// <summary>Snapshots a trusted execution command before asynchronous admission or retained use.</summary>
    /// <param name="operation">Execution command whose subject was bound by the server boundary.</param>
    /// <returns>An independently owned command and generated execution input.</returns>
    public Command<DatabaseCommand> OwnCommand(Command<DatabaseCommand> operation)
        => SnapshotCommand(operation, Limits);

    internal static Command<DatabaseCommand> SnapshotCommand(Command<DatabaseCommand> operation, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateDatabaseCommand(operation, limits);
        using var buffer = new CommandSnapshotBuffer(limits.MaxBatchBytes);
        OrleansSerialization.Serialize(operation, buffer);
        var owned = OrleansSerialization.Deserialize<Command<DatabaseCommand>>(buffer.GetBuffer().AsSpan(SnapshotStart, checked((int)buffer.Length)));
        ValidateDatabaseCommand(owned, limits);
        return owned;
    }

    private static void ValidateDatabaseCommand(Command<DatabaseCommand> operation, DatabaseLimits limits)
    {
        var input = operation.Value;
        if (operation.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(operation.UserId)
            || operation.CommandType != DatabaseCommandType || operation.Timestamp.Kind != DateTimeKind.Utc
            || input is null || input.Input.IsEmpty || !Enum.IsDefined(input.Kind)
            || string.IsNullOrEmpty(input.Fingerprint) || input.Fingerprint.Length != FingerprintCharacters
            || input.Fingerprint.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDatabaseCommand);
        }
        RequireBudget(checked((long)input.Input.Length + input.RetryDecisions.Length + input.ResourceAdmission.Length), limits.MaxBatchBytes);
        OrleansSerialization.Validate(input.Input.Span, OperationPayloadType(input.Kind)
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand));
    }

    private Command<DatabaseCommand> CreatePreparedCommand(OperationKind kind, Guid id, string principalId,
        DateTimeOffset evaluatedAt, ReadOnlyMemory<byte> input, string fingerprint, ReadExecutionBudget? work,
        IKeyValueView? admissionView = null)
    {
        var operation = Command<DatabaseCommand>.Create(DatabaseCommandType,
            new DatabaseCommand(kind, input, fingerprint), id);
        operation.UserId = principalId;
        operation.Timestamp = evaluatedAt.UtcDateTime;
        byte[] admission;
        if (admissionView is not null)
        {
            if (RequiresCompositionEffectCapture(operation))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDatabaseCommand);
            }
            work?.Check();
            admission = EncodeResourceEffectAdmission(CaptureResourceEffectAdmission(admissionView, operation, fingerprint,
                preserveBudgetFailure: work is not null));
            work?.Check();
        }
        else
        {
            admission = work is null
                ? CaptureResourceEffectAdmission(operation)
                : CaptureResourceEffectAdmission(operation, work);
        }
        operation.Value = operation.Value! with
        {
            ResourceAdmission = admission
        };
        return OwnCommand(operation);
    }

    private Command<DatabaseCommand> EvaluateCommand(Command<DatabaseCommand> operation, DateTimeOffset evaluatedAt)
    {
        var evaluated = OwnCommand(operation);
        evaluated.Timestamp = evaluatedAt.UtcDateTime;
        return evaluated;
    }

    private void RequireBudget(long byteCount)
        => RequireBudget(byteCount, Limits.MaxBatchBytes);

    private static void RequireBudget(long byteCount, int maximumBytes)
    {
        if (byteCount > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidCommandBudgetMessage);
        }
    }
}
