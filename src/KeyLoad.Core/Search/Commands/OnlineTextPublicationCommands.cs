using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private Result<object> ExecuteOnlineTextPublication(IAtomicTransaction transaction,
        PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var command = Payload<OnlineTextPublicationPhaseCommand>(operation);
        RequireEnvelopeId(operation, command.Request.CommandId);
        ValidateOnlineTextCanonicalSource(transaction, principal, command);
        var key = OnlineTextPublicationKeys.Current(command.Request);
        var previous = transaction.GetRecord<OnlineTextCurrentPublication>(key);
        if (previous?.CommandId != command.ExpectedCurrentCommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.InvalidPublication);
        }
        var authority = OnlineTextPublicationValidation.Authority(principal.Id, command);
        transaction.PutRecord(key, new OnlineTextCurrentPublication(OnlineTextPublicationProtocol.CurrentFormat,
            principal.Id, command.Request.CommandId, authority.ParentFingerprint, command.Request.Consumer,
            command.Request.ConsumerGeneration, authority, command.Result.PublishedCut));
        return Result(command.Result);
    }

    private static OnlineTextOutcomeAuthority? CaptureOnlineTextOutcomeAuthority(Command<DatabaseCommand> operation)
        => operation.Value!.Kind == OperationKind.OnlineTextPublicationPhase
            ? OnlineTextPublicationValidation.Authority(operation.UserId!,
                Payload<OnlineTextPublicationPhaseCommand>(operation)) : null;
}
