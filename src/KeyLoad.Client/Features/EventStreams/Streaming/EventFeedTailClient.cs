using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Creates the authenticated SDK transport with explicit finite native streaming policy.</summary>
    /// <param name="http">The existing caller-owned HTTP transport.</param>
    /// <param name="apiKey">The persisted database credential.</param>
    /// <param name="executionOptions">The existing central terminal transport policy.</param>
    /// <param name="streamingOptions">The central native frame/failure/total streaming policy.</param>
    public KeyLoadClient(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions,
        IOptions<CqrsStreamClientOptions> streamingOptions)
    {
        ArgumentNullException.ThrowIfNull(streamingOptions);
        transport = new Features.ClientApi.KeyLoadClientTransport(http, apiKey, executionOptions, streamingOptions);
    }

    internal Features.ClientApi.KeyLoadClientTransport HttpTransport => transport;
}
