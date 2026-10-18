using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Routes every journal operation through a fresh protected native request.</summary>
internal sealed class RuntimeJournalClient(
    IGrainFactory grains,
    Func<GrainRequestCodec> codecFactory,
    Func<DatabaseEngine> databaseFactory,
    IServiceProvider services,
    TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer,
    IOptions<GrainRoutingOptions> routingOptions,
    IOptions<RuntimeJournalOptions> configuredJournalOptions,
    RuntimeJournalAdmission admission)
{
    private readonly IOptions<RuntimeJournalOptions> journalOptions = ValidateOptions(configuredJournalOptions);

    internal static string CallerIdentity => typeof(RuntimeJournalClient).FullName!;
    internal const string ReadCoreCallerMethod = nameof(ReadCoreAsync);
    internal const string SendCommandCallerMethod = nameof(SendCommandAsync);

    internal TimeProvider Clock => clock;

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
        using var timeout = new CancellationTokenSource(routingOptions.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = deadline.Token;
        var codec = await PrepareAsync(payload, token).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalRead(requestId, kind, payload);
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = GrainRequestIdentityScope.ForRuntimeJournal(services, requestId,
            Guid.Empty, token, connectionId);
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var reply = await DrainAsRuntimeJournalCallerAsync(ReadCoreCallerMethod, () =>
            GrainRequestStreamConsumer.DrainAsync(
                createStream: streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer: chunkSerializer, requestId: requestId,
                clock: clock, cancellationToken: token, options: routingOptions)).ConfigureAwait(false);
        return Decode<T>(reply, allowNull);
    }

    private async Task<GrainOperationReply> SendCommandAsync(ReadOnlyMemory<byte> payload, Guid commandId,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(routingOptions.Value.ExecutionLifetime, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = deadline.Token;
        var codec = await PrepareAsync(payload, token).ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var signed = codec.CreateRuntimeJournalCommand(requestId, RuntimeJournalIdentity.ProtectedPrincipalId, commandId, payload);
        var connectionId = NativeConnectionExecutionIdentity.Resolve(services);
        using var identity = GrainRequestIdentityScope.ForRuntimeJournal(services, requestId, commandId, token, connectionId);
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        return await DrainAsRuntimeJournalCallerAsync(SendCommandCallerMethod, () =>
            GrainRequestStreamConsumer.DrainAsync(
                createStream: streamToken => actor.ExecuteStreamAsync(signed, streamToken), serializer: chunkSerializer, requestId: requestId,
                clock: clock, cancellationToken: token, options: routingOptions)).ConfigureAwait(false);
    }

    private static async Task<T> DrainAsRuntimeJournalCallerAsync<T>(string callerMethod, Func<Task<T>> drain)
    {
        T result = default!;
        await RequestContextHelper.RunWithCurrentCallerAsync(CallerIdentity, callerMethod, async () =>
        {
            result = await drain().ConfigureAwait(false);
        }).ConfigureAwait(false);
        return result;
    }

    private async Task<GrainRequestCodec> PrepareAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await admission.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var database = databaseFactory();
        ValidateDatabaseLimits(database, payload);
        return codecFactory();
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

    private static void ValidateDatabaseLimits(DatabaseEngine database, ReadOnlyMemory<byte> payload)
    {
        // The signed-token wrapper has its own additive envelope allowance. Bound the complete native DTO here.
        if (payload.Length > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }
    }

    private static IOptions<RuntimeJournalOptions> ValidateOptions(IOptions<RuntimeJournalOptions> configuredOptions)
    {
        var options = configuredOptions.Value;
        if (!options.IsValid())
        {
            throw new InvalidOperationException(RuntimeJournalStoragePolicy.InvalidLimits);
        }

        return configuredOptions;
    }
}
