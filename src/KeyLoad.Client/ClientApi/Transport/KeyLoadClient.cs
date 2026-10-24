using KeyLoad.Client.ClientApi;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>HTTP SDK. Writes keep caller command IDs; a timeout is an unknown outcome, never an automatic new write.</summary>
/// <remarks>Creates the authenticated transport with one validated execution policy snapshot.</remarks>
/// <param name="http">The caller-owned HTTP transport.</param>
/// <param name="apiKey">The persisted database credential.</param>
/// <param name="executionOptions">The caller's centrally bound transport budget.</param>
public sealed partial class KeyLoadClient(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions)
{
    private readonly KeyLoadClientTransport transport = new(http, apiKey, executionOptions);

    internal KeyLoadClientExecutionOptions ExecutionOptions => transport.ExecutionOptions;

    internal Task<Result<T>> Send<T>(string path, object? request, bool write, Guid? id, CancellationToken cancellationToken,
        HttpMethod? method = null)
        => transport.Send<T>(path, request, write, id, cancellationToken, method);
}
