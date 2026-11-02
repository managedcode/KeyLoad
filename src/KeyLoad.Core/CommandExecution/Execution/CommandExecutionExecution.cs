using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionExecution operations against the caller's database context.</summary>
public static class CommandExecutionExecution
{
    /// <summary>Creates the execution communication command from its typed database input.</summary>
    /// <param name = "kind">The exact command capability.</param>
    /// <param name = "id">Stable command identity.</param>
    /// <param name = "principalId">Persisted principal selected by the caller's trusted boundary.</param>
    /// <param name = "evaluatedAt">Original operation evaluation time.</param>
    /// <param name = "input">The typed input contract of this capability.</param>
    /// <returns>An owned execution command.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, null) /* KLD0041: Command creation admits the exact Kind-selected input into the existing native persisted envelope without JSON conversion. */;
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        work.Check();
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, work) /* KLD0041: Command creation admits the exact Kind-selected input into the existing native persisted envelope without JSON conversion. */;
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, IKeyValueView admissionView, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input)
    {
        ArgumentNullException.ThrowIfNull(admissionView) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, null, admissionView) /* KLD0041: Command creation admits the exact Kind-selected input into the existing native persisted envelope without JSON conversion. */;
    }

    internal static Command<DatabaseCommand> CreateCommandCore(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ input, ReadExecutionBudget? work, IKeyValueView? admissionView = null)
    {
        if (kind is OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand);
        }

        var /* KLD0041: This native command admission receives the exact Kind-selected business input before storing it in the existing persisted envelope. */ admitted = DatabaseCommandInputTypes.Admit(kind, input) /* KLD0041: The closed operation-kind map validates the exact runtime contract held by the existing persisted input slot. */;
        return engine.CreatePreparedCommand(kind, id, principalId, evaluatedAt, admitted, work, admissionView) /* KLD0041: This admission preserves the Kind-selected native payload in the existing persisted command envelope. */;
    }
}
