namespace KeyLoad.Orleans;

/// <summary>Configured receiving-owner transport invoked only by the existing authorized read actor.</summary>
public interface IRemoteDocumentReadRouter
{
    /// <summary>Routes one document under the original source envelope and fresh source subject.</summary>
    /// <param name="envelope">Original verified actor envelope.</param>
    /// <param name="principal">Fresh persisted source subject; grants are never forwarded.</param>
    /// <param name="request">Original document scope and optional minimum.</param>
    /// <param name="cancellationToken">Original capability cancellation.</param>
    /// <returns>Only the authorized projected document, after source-fence revalidation.</returns>
    Task<DocumentResult?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        GetDocumentRequest request, CancellationToken cancellationToken);
}
