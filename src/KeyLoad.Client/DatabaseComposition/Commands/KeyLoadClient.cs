using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Submits one atomic command using its stable idempotency identifier.</summary>
    /// <param name="command">Typed command and caller-owned stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed receipt or a classified transport/server problem.</returns>
    public Task<Result<CommitReceipt>> CommitAsync(CommandRequest command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Send<CommitReceipt>(ClientApiRoutes.Commands, command, true, command.CommandId, cancellationToken);
    }
    /// <summary>Creates or updates a persisted resource definition.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="request">Resource schema and authorization policy.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting resource definition.</returns>
    public Task<Result<ResourceDefinition>> ConfigureResourceAsync(Guid commandId, ConfigureResourceRequest request, CancellationToken cancellationToken = default)
        => Send<ResourceDefinition>(ClientApiRoutes.ResourcesConfigure, request, true, commandId, cancellationToken);
}
