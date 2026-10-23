using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Creates or updates one persisted principal.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="principal">Principal identity and policy data.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting principal record.</returns>
    public Task<Result<PrincipalRecord>> ConfigurePrincipalAsync(Guid commandId, PrincipalRecord principal, CancellationToken cancellationToken = default)
        => Send<PrincipalRecord>(ApiRoutes.PrincipalsConfigure, new ConfigurePrincipalRequest(principal), true, commandId, cancellationToken);
    /// <summary>Creates or updates one persisted API key.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="key">API key metadata and policy.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>No-data acknowledgement after the original execution credential write succeeds.</returns>
    public Task<Result> ConfigureApiKeyAsync(Guid commandId, ApiKeyRecord key, CancellationToken cancellationToken = default)
        => transport.SendStatus(ApiRoutes.ApiKeysConfigure, new ConfigureApiKeyRequest(key), commandId, cancellationToken);
}
