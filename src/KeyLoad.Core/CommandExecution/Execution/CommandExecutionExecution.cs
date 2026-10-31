using System.Text.Json;
using KeyLoad.Core.InternalSerialization;
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
    /// <returns>An owned execution command retaining the original input fingerprint.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object input)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, null);
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object input, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, work);
    }

    internal static Command<DatabaseCommand> CreateCommand(this DatabaseEngine engine, IKeyValueView admissionView, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object input)
    {
        ArgumentNullException.ThrowIfNull(admissionView);
        return engine.CreateCommandCore(kind, id, principalId, evaluatedAt, input, null, admissionView);
    }

    internal static Command<DatabaseCommand> CreateCommandCore(this DatabaseEngine engine, OperationKind kind, Guid id, string principalId, DateTimeOffset evaluatedAt, object input, ReadExecutionBudget? work, IKeyValueView? admissionView = null)
    {
        if (kind is OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand);
        }

        var admitted = DatabaseCommandInputTypes.Require(kind, input);
        var identity = admitted is CommandRequest batch ? BatchIdentity(batch, work) : Identity(admitted);
        return engine.CreatePreparedCommand(kind, id, principalId, evaluatedAt, admitted, DatabaseCommandFingerprint.Compute(id, kind, principalId, identity), work, admissionView);
    }

    internal static string BatchIdentity(CommandRequest request, ReadExecutionBudget? work)
    {
        if (!AnalyticsIdentity.HasAnalyticsMutation(request))
        {
            return Identity(request);
        }

        if (work is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnalyticsWorkRequired);
        }

        return AnalyticsIdentity.Canonicalize(request, work);
    }

    internal const string AnalyticsWorkRequired = "Execution analytics issuance requires the original operation budget.";
    internal static string Identity(object input) => JsonSerializer.Serialize(input, input.GetType(), JsonDefaults.Options);
}
