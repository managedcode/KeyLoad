using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Uses the existing authenticated transport for explicit follower committed-snapshot reads.</summary>
public static class FollowerDocumentClient
{
    private const string Route = "/v1/documents/read-follower";

    /// <summary>Reads an explicitly bounded older data cut under fresh persisted credential and policy authority.</summary>
    /// <param name="client">The actual authenticated SDK client.</param>
    /// <param name="request">Versioned follower, exact document, maximum position lag and optional acknowledged minimum.</param>
    /// <param name="cancellationToken">Cancels the one bounded native operation.</param>
    /// <returns>Explicit data/authorization cuts and a full nullable document, or a safe refusal without a value.</returns>
    public static Task<Result<FollowerDocumentReadResultV1>> ReadFollowerDocumentAsync(this KeyLoadClient client,
        ReadFollowerDocumentRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<FollowerDocumentReadResultV1>(Route, request, false, null, cancellationToken);
    }
}
