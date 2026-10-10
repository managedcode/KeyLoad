using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateOnlineTextPublicationOperation(string principalId,
        DateTimeOffset evaluatedAt, OnlineTextPreparedPublication prepared, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(budget);
        var command = prepared.Command;
        OnlineTextPublicationShape.Require(command, Limits.MaxScanRecords);
        if (EvaluationClock.GetUtcNow() >= prepared.OriginalExpiry)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, OnlineTextPublicationProtocol.InvalidPublication); }
        return Store.Read(raw =>
        {
            budget.ConstrainLifetime(prepared.OriginalExpiry);
            var principal = Principal(budget.CreateView(raw), principalId, evaluatedAt);
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, OnlineTextPublicationProtocol.AdministratorRequired); }
            var pin = new NativeTextSeedPin(command.Request.Consumer, command.Request.ConsumerGeneration,
                command.Request.Collection, command.Request.Field, command.Request.NodeId, command.Request.Placement);
            var current = NativeTextSeedCollector.CaptureMetadataView(this, raw, principalId, pin, budget);
            OnlineTextPublicationValidation.LocalSource(command, current, prepared.ValidatedSource);
            ValidateOnlineTextCheckpoint(raw, principal, command.Request, command.Result.Checkpoint, prepared.CheckpointIntent);
            budget.Check();
            var payload = NativeSerialization.Serialize(command);
            RequireNativeBudget(payload.Length);
            return IssueNativeOperation(new(command.Request.CommandId, OperationKind.OnlineTextPublicationPhase,
                principalId, evaluatedAt, Identity<OnlineTextPublicationPhaseCommand>(payload)), new NativeCommandPayload(payload));
        });
    }
}
