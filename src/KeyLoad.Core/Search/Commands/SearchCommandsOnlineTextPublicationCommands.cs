using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SearchCommandsOnlineTextPublicationCommands operations against the caller's database context.</summary>
public static class SearchCommandsOnlineTextPublicationCommands
{
    internal static Result<object> ExecuteOnlineTextPublication(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var command = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation);
        OperationProtocol.RequireEnvelopeId(operation, command.Request.CommandId);
        engine.ValidateOnlineTextCanonicalSource(transaction, principal, command);
        var key = OnlineTextPublicationKeys.Current(command.Request);
        var previous = transaction.GetRecord<OnlineTextCurrentPublication>(key);
        if (previous?.CommandId != command.ExpectedCurrentCommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.InvalidPublication);
        }

        var authority = OnlineTextPublicationValidation.Authority(principal.Id, command);
        transaction.PutRecord(key, new OnlineTextCurrentPublication(OnlineTextPublicationProtocol.CurrentFormat, principal.Id, command.Request.CommandId, authority.ParentFingerprint, command.Request.Consumer, command.Request.ConsumerGeneration, authority, command.Result.PublishedCut));
        return OperationProtocol.Result(command.Result);
    }

    internal static OnlineTextOutcomeAuthority? CaptureOnlineTextOutcomeAuthority(Command<DatabaseCommand> operation) => operation.Value!.Kind == OperationKind.OnlineTextPublicationPhase ? OnlineTextPublicationValidation.Authority(operation.UserId!, OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation)) : null;
}
