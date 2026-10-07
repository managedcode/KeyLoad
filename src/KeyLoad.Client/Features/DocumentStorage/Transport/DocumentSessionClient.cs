using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads one document at or beyond an acknowledged token using fresh quorum authority.</summary>
    /// <param name="reference">Exact document and atomic partition identity.</param>
    /// <param name="minimumToken">Acknowledged position in the current physical incarnation and placement.</param>
    /// <param name="cancellationToken">Cancellation of the bounded public operation.</param>
    /// <returns>The complete authorized result or an explicit invalid token or authority problem.</returns>
    public Task<Result<DocumentResult?>> GetAsync(EntityRef reference, CommitToken minimumToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(minimumToken);
        return Send<DocumentResult?>(ClientApiRoutes.DocumentsGet, new GetDocumentRequest(reference, minimumToken),
            false, null, cancellationToken);
    }
}
