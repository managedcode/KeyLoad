using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeInbox(IKeyValueView view, PrincipalRecord principal, CommitInboxRequest request)
    {
        ValidateInboxInput(request);
        Authorization.Require(principal, request.Target.Partition, request.Target.Queue, Capability.InboxWrite);
        var resource = Resource(view, request.Target.Partition, request.Target.Queue, ResourceKind.WorkQueue);
        ValidateInboxPolicy(resource);
        if (resource.InboxPolicy is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, TargetInboxProtocol.Unavailable); }
        _ = AuthorizeBatch(view, principal, new(request.CommandId, request.Target.Partition, request.Effects), allowEmpty: true);
    }
}
