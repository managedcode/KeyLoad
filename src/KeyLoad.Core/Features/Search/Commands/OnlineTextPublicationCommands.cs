using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteOnlineTextPublication(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var command = Payload<OnlineTextPublicationPhaseCommand>(operation);
        RequireEnvelopeId(operation, command.Request.CommandId);
        ValidateOnlineTextCanonicalSource(transaction, principal, command);
        var key = OnlineTextPublicationKeys.Current(command.Request);
        var previous = transaction.GetRecord<OnlineTextCurrentPublication>(key);
        if (previous?.CommandId != command.ExpectedCurrentCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.InvalidPublication); }
        var authority = OnlineTextPublicationValidation.Authority(principal.Id, command);
        transaction.PutRecord(key, new OnlineTextCurrentPublication(OnlineTextPublicationProtocol.CurrentFormat,
            principal.Id, command.Request.CommandId, authority.ParentFingerprint, command.Request.Consumer,
            command.Request.ConsumerGeneration, authority, command.Result.PublishedCut));
        return Result(command.Result);
    }

    private static OnlineTextOutcomeAuthority? CaptureOnlineTextOutcomeAuthority(ReplicatedOperation operation)
        => operation.Kind == OperationKind.OnlineTextPublicationPhase
            ? OnlineTextPublicationValidation.Authority(operation.PrincipalId,
                Payload<OnlineTextPublicationPhaseCommand>(operation)) : null;
}
