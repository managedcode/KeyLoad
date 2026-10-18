using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Verifies and bootstraps private journal authority before provider admission.</summary>
internal sealed class RuntimeJournalStartupRequests(IGrainFactory grains, GrainRequestCodec codec,
    IServiceProvider services, TimeProvider clock,
    IOptions<GrainRoutingOptions> routingOptions)
{
    private const string InvalidCatalogReply = "The runtime journal catalog reply is invalid.";
    private const string AuthorityNotCommitted = "Runtime journal authority was not committed.";
    private const string StartupRequestFailed = "Runtime journal startup request failed.";
    private const long InitialOwnerGeneration = 0;
    private const long InitialContentRevision = 0;
    internal async Task<bool> TryVerifyAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalRead(requestId, GrainReadKind.RuntimeJournalCatalog,
            NativeSerialization.Serialize(GrainNativeContracts.NoDtoMarker));
        var reply = await SendAsync(null, requestId, Guid.Empty, signed, cancellationToken).ConfigureAwait(false);
        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value;
        return value switch
        {
            null => false,
            RuntimeJournalCatalog => true,
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidCatalogReply)
        };
    }

    internal async Task BootstrapAsync(PrincipalRecord administrator, Guid commandId,
        CancellationToken cancellationToken)
    {
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.BootstrapIdentity, string.Empty,
            Guid.Empty, InitialOwnerGeneration, InitialContentRevision, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalCommand(requestId, administrator.Id, commandId,
            NativeSerialization.Serialize(mutation));
        _ = await SendAsync(administrator, requestId, commandId, signed, cancellationToken).ConfigureAwait(false);
        if (!await TryVerifyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AuthorityNotCommitted);
        }
    }

    private async Task<GrainOperationReply> SendAsync(PrincipalRecord? principal, Guid requestId,
        Guid commandId, string signed, CancellationToken cancellationToken)
    {
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = principal is null
            ? GrainRequestIdentityScope.ForRuntimeJournal(services, requestId, commandId, cancellationToken, connectionId)
            : new GrainRequestIdentityScope(services, principal, requestId, commandId, cancellationToken,
                connectionId: connectionId);
        var serializer = services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            createStream: token => grains.GetGrain<IConnectionGrain>(connectionId).ExecuteStreamAsync(signed, token), serializer: serializer,
            requestId: requestId, clock: clock, cancellationToken: cancellationToken, options: routingOptions).ConfigureAwait(false);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? StartupRequestFailed);
        }
        return reply;
    }
}
