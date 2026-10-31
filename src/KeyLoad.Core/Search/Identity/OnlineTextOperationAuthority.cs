using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextOperationAuthority operations against the caller's database context.</summary>
public static class OnlineTextOperationAuthority
{
    internal static Command<DatabaseCommand> CreateOnlineTextPublicationOperation(this DatabaseEngine engine, string principalId, DateTimeOffset evaluatedAt, OnlineTextPreparedPublication prepared, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(budget);
        var command = prepared.Command;
        OnlineTextPublicationShape.Require(command);
        return engine.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = engine.Principal(view, principalId, evaluatedAt);
            if (!principal.ClusterAdministrator)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, OnlineTextPublicationProtocol.AdministratorRequired);
            }

            var pin = new TextSeedPin(command.Request.Consumer, command.Request.ConsumerGeneration, command.Request.Collection, command.Request.Field, command.Request.NodeId, command.Request.Placement);
            var current = TextSeedCollector.CaptureMetadataView(engine, raw, principalId, pin, budget);
            OnlineTextPublicationValidation.LocalSource(command, current, prepared.ValidatedSource);
            engine.ValidateOnlineTextCheckpoint(raw, principal, command.Request, command.Result.Checkpoint, prepared.CheckpointIntent);
            budget.Check();
            return engine.CreatePreparedCommand(OperationKind.OnlineTextPublicationPhase, command.Request.CommandId, principalId, evaluatedAt, command, DatabaseCommandFingerprint.Compute(command.Request.CommandId, OperationKind.OnlineTextPublicationPhase, principalId, CommandExecutionExecution.Identity(command)), budget, view);
        });
    }
}
