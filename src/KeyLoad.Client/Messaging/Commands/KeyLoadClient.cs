using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Commits target inbox and effects; source acknowledgement remains a separate operation.</summary>
    /// <param name="request">The declared dedup identity and bounded target effects.</param>
    /// <param name="cancellationToken">The original caller cancellation token.</param>
    /// <returns>The actual execution target receipt and explicit replay status.</returns>
    public Task<Result<CommitInboxResult>> CommitInboxAsync(CommitInboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request CommitInboxRequest reference through its object parameter. */;
        return Send<CommitInboxResult, CommitInboxRequest>(ApiRoutes.InboxCommit, request, true, request.CommandId, cancellationToken);
    }
}
