using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Routes every journal operation through a fresh protected native request.</summary>
internal sealed class RuntimeJournalClient(
    IGrainFactory grains,
    GrainRequestCodec codec,
    DatabaseEngine database,
    ICommitCoordinator coordinator,
    IServiceProvider services,
    TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
    IOptions<GrainRoutingOptions> routingOptions,
    IOptions<RuntimeJournalOptions> configuredJournalOptions,
    RuntimeJournalAdmission admission)
{
    private readonly IOptions<RuntimeJournalOptions> journalOptions = ValidateOptions(configuredJournalOptions,
        database.Limits.MaxBatchBytes);

    internal async Task<RuntimeJournalSnapshot?> GetHeaderAsync(string name, CancellationToken cancellationToken)
    {
        var payload = NativeSerialization.Serialize(name);
        return await ReadNullableAsync<RuntimeJournalSnapshot>(GrainReadKind.RuntimeJournalHeader, payload,
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<RuntimeJournalPage> ReadPageAsync(RuntimeJournalSnapshot snapshot, long offset,
        CancellationToken cancellationToken)
        => ReadAsync<RuntimeJournalPage>(GrainReadKind.RuntimeJournalPage,
            NativeSerialization.Serialize(new RuntimeJournalReadRequest(snapshot.JournalName, snapshot.InstanceId,
                snapshot.OwnerGeneration, snapshot.ContentRevision, offset)), cancellationToken);

    internal Task<RuntimeJournalCatalog> GetCatalogAsync(CancellationToken cancellationToken)
        => ReadAsync<RuntimeJournalCatalog>(GrainReadKind.RuntimeJournalCatalog,
            NativeSerialization.Serialize(GrainNativeContracts.NoDtoMarker), cancellationToken);

    internal async Task<RuntimeJournalMutationResult> MutateAsync(RuntimeJournalMutation mutation,
        CancellationToken cancellationToken)
    {
        var payload = NativeSerialization.Serialize(mutation);
        var commandId = Guid.NewGuid();
        var reply = await SendCommandAsync(payload, commandId, cancellationToken).ConfigureAwait(false);
        if (reply.Error == ErrorCode.UnknownWriteOutcome
            && journalOptions.Value.UncertaintyRetryCount > RuntimeJournalStoragePolicy.NoUncertaintyRetryCount
            && !cancellationToken.IsCancellationRequested)
        {
            reply = await SendCommandAsync(payload, commandId, cancellationToken).ConfigureAwait(false);
        }

        return Decode<RuntimeJournalMutationResult>(reply, allowNull: false)
            ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);
    }

    private async Task<T> ReadAsync<T>(GrainReadKind kind, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) where T : class
        => await ReadCoreAsync<T>(kind, payload, allowNull: false, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);

    private Task<T?> ReadNullableAsync<T>(GrainReadKind kind, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) where T : class
        => ReadCoreAsync<T>(kind, payload, allowNull: true, cancellationToken: cancellationToken);

    private async Task<T?> ReadCoreAsync<T>(GrainReadKind kind, ReadOnlyMemory<byte> payload,
        bool allowNull, CancellationToken cancellationToken) where T : class
    {
        using var deadline = CreateDeadline(cancellationToken);
        var token = deadline.Token;
        var principal = await PrepareAsync(token).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalRead(requestId, kind, payload);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId,
            Guid.Empty, token);
        var actor = grains.GetGrain<IRequestGrain>(requestId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            createStream: streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer: chunkSerializer, requestId: requestId,
            clock: clock, cancellationToken: token, options: routingOptions).ConfigureAwait(false);
        return Decode<T>(reply, allowNull);
    }

    private async Task<GrainOperationReply> SendCommandAsync(ReadOnlyMemory<byte> payload, Guid commandId,
        CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        var token = deadline.Token;
        var principal = await PrepareAsync(token).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalCommand(requestId, principal.Id, commandId, payload);
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, commandId, token);
        var actor = grains.GetGrain<IRequestGrain>(requestId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer: chunkSerializer, requestId: requestId,
            clock: clock, cancellationToken: token, options: routingOptions).ConfigureAwait(false);
    }

    private async Task<PrincipalRecord> PrepareAsync(CancellationToken cancellationToken)
    {
        await admission.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadProtectedPrincipal();
    }

    private PrincipalRecord ReadProtectedPrincipal()
    {
        var principal = database.Store.Read(view =>
            database.Principal(view, RuntimeJournalIdentity.ProtectedPrincipalId, clock.GetUtcNow()));
        RuntimeJournalIdentity.RequireProtected(principal);
        return principal;
    }

    private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(routingOptions.Value.ExecutionLifetime);
        return deadline;
    }

    private static T? Decode<T>(GrainOperationReply reply, bool allowNull = false) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RuntimeJournalStoragePolicy.RequestFailed);
        }

        var value = GrainNativePayload.Read<GrainValue>(reply.Payload).Value;
        if (value is null && allowNull)
        {
            return null;
        }

        return value as T ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);
    }

    private static IOptions<RuntimeJournalOptions> ValidateOptions(IOptions<RuntimeJournalOptions> configuredOptions,
        int maximumBatchBytes)
    {
        var options = configuredOptions.Value;
        if (!options.IsValid()
            || options.MaximumJournalBytes > maximumBatchBytes - GrainRoutingProtocol.EnvelopeMetadataBytes)
        {
            throw new InvalidOperationException(RuntimeJournalStoragePolicy.InvalidLimits);
        }

        return configuredOptions;
    }
}
