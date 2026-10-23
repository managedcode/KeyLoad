using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
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
        OnlineTextPublicationShape.Require(command, engine.Limits.MaxScanRecords);
        if (engine.EvaluationClock.GetUtcNow() >= prepared.OriginalExpiry)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, OnlineTextPublicationProtocol.InvalidPublication);
        }

        return engine.Store.Read(raw =>
        {
            budget.ConstrainLifetime(prepared.OriginalExpiry);
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
            var payload = OrleansSerialization.Serialize(command);
            engine.RequireBudget(payload.Length);
            return engine.CreatePreparedCommand(OperationKind.OnlineTextPublicationPhase, command.Request.CommandId, principalId, evaluatedAt, payload, DatabaseCommandFingerprint.Compute(command.Request.CommandId, OperationKind.OnlineTextPublicationPhase, principalId, CommandExecutionExecution.Identity<OnlineTextPublicationPhaseCommand>(payload)), budget, view);
        });
    }
}
