using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns one server-issued connection and joins its independent operations through native Orleans streams.</summary>
public sealed class ConnectionGrain(GrainRequestCodec codec, SiloDatabaseClient storage,
    DatabaseEngine database, IOptions<GrainRoutingOptions> options)
    : Grain, IConnectionGrain, IAsyncDisposable
{
    private readonly NativeConnectionOperationOwner operations = new(options);
    internal CancellationToken AdmissionClosedToken => operations.ShutdownToken;

    /// <inheritdoc />
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
    {
        var request = codec.Verify(signedRequest);
        GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
        return NativeConnectionOperationStream.Run(operations, request.Envelope.RequestId,
            token => storage.ExecuteStreamAsync(database.Store.Identity.Incarnation, signedRequest, token), cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(string signedControl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.VerifyConnectionClose(signedControl, this.GetPrimaryKey());
        try
        {
            await operations.CloseAsync().ConfigureAwait(true);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        var owned = await NativeRequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true);
        var native = await NativeRequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true);
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(owned, native));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();
}
