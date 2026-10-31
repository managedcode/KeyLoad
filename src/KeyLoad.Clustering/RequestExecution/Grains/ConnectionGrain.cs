using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Placement;

namespace KeyLoad.Clustering;

/// <summary>Owns one server-issued connection; every admitted operation runs in its own request grain.</summary>
[Immovable(ImmovableKind.Any), PreferLocalPlacement]
public sealed class ConnectionGrain(GrainRequestCodec codec, IOptions<GrainRoutingOptions> options)
    : Grain, IConnectionGrain, IAsyncDisposable
{
    private readonly ConnectionOperationOwner operations = new(options);
    internal CancellationToken AdmissionClosedToken => operations.ShutdownToken;

    /// <inheritdoc />
    public async Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var requestId = Admit(envelope);
        using var admitted = operations.Acquire(requestId);
        return await Request(requestId).ExecuteAsync(envelope, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        GrainRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var requestId = Admit(envelope);
        return ReleaseAfter(ConnectionOperationStream.Run(operations, requestId,
            token => Request(requestId).ExecuteStreamAsync(envelope, token), cancellationToken), requestId, cancellationToken);
    }

    private async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ReleaseAfter(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream, Guid requestId,
        CancellationToken cancellationToken, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
            await foreach (var chunk in stream.WithCancellation(linked.Token).ConfigureAwait(true))
            {
                yield return chunk;
            }
        }
        finally
        {
            await Request(requestId).ReleaseAsync().ConfigureAwait(true);
        }
    }

    private Guid Admit(GrainRequestEnvelope envelope)
    {
        var request = codec.Decode(envelope);
        GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
        return request.Envelope.RequestId;
    }

    private IRequestGrain Request(Guid requestId)
        => GrainFactory.GetGrain<IRequestGrain>(RequestExecutionGrainIdentity.For(requestId));

    /// <inheritdoc />
    public async Task CloseAsync(GrainConnectionCloseControl closeControl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.ValidateConnectionClose(closeControl, this.GetPrimaryKey());
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
        var owned = (await RequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true)).Value;
        var execution = (await RequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true)).Value;
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(owned, execution));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();
}
