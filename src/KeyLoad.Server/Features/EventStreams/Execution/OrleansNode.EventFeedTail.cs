using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    internal Task<GrainOperationReply> ExecuteTailWithProgressAsync(Guid requestId, string signedRequest,
        Func<GrainRequestProgress, CancellationToken, ValueTask> progress, CancellationToken cancellationToken)
        => requests.ExecuteWithProgressAsync(CurrentRequestTarget, requestId, signedRequest, progress, cancellationToken);
}
