using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPreparedOperationV1 RequirePreparedResourceRetirementCleanupPage(IKeyValueView view,
        ReplicatedOperation original, ResourceRetirementCleanupPageBodyV1 body)
    {
        var context = ResourceRetirementCleanupAttemptScope.Context(body);
        var attempt = body.Attempt ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        return RequireExactPreparedResourceRetirementOperation(view, original, context, attempt);
    }
}
