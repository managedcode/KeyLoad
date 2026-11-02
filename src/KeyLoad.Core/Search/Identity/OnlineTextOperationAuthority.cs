using KeyLoad.Core.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextOperationAuthority operations against the caller's database context.</summary>
public static class OnlineTextOperationAuthority
{
    internal static Command<DatabaseCommand> CreateOnlineTextPublicationOperation(this DatabaseEngine engine, string principalId, DateTimeOffset evaluatedAt, OnlineTextPreparedPublication prepared, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(prepared) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
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
            return engine.CreatePreparedCommand(OperationKind.OnlineTextPublicationPhase, command.Request.CommandId, principalId, evaluatedAt, command, budget, view) /* KLD0041: This admission preserves the Kind-selected native payload in the existing persisted command envelope. */;
        });
    }
}
