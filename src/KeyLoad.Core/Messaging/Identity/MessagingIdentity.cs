using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingIdentity operations against the caller's database context.</summary>
public static class MessagingIdentity
{
    internal static void AuthorizeInbox(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, CommitInboxRequest request)
    {
        TargetInboxValidation.ValidateInboxInput(request);
        engine.Authorization.Require(principal, request.Target.Partition, request.Target.Queue, Capability.InboxWrite);
        var resource = engine.Resource(view, request.Target.Partition, request.Target.Queue, ResourceKind.WorkQueue);
        TargetInboxValidation.ValidateInboxPolicy(resource);
        if (resource.InboxPolicy is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, TargetInboxProtocol.Unavailable);
        }

        _ = engine.AuthorizeBatch(view, principal, new(request.CommandId, request.Target.Partition, request.Effects), allowEmpty: true);
    }
}
