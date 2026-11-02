using KeyLoad.Core;
using KeyLoad.Core.RuntimeJournaling.Identity;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Routes every journal operation through a fresh protected execution request.</summary>
internal sealed class RuntimeJournalClient(
    IGrainFactory grains,
    Func<GrainRequestCodec> codecFactory,
    IServiceProvider services,
    TimeProvider clock,
    IOptions<RuntimeJournalOptions> configuredJournalOptions,
    RuntimeJournalAdmission admission)
{
    private readonly IOptions<RuntimeJournalOptions> journalOptions = ValidateOptions(configuredJournalOptions);

    internal TimeProvider Clock => clock;

    internal async Task<RuntimeJournalSnapshot?> GetHeaderAsync(string name, CancellationToken cancellationToken)
    {
        var payload = name;
        return await ReadNullableAsync<RuntimeJournalSnapshot, string>(GrainReadKind.RuntimeJournalHeader, payload,
            cancellationToken);
    }

    internal Task<RuntimeJournalPage> ReadPageAsync(RuntimeJournalSnapshot snapshot, long offset,
        CancellationToken cancellationToken)
        => ReadAsync<RuntimeJournalPage, RuntimeJournalReadRequest>(GrainReadKind.RuntimeJournalPage,
            new RuntimeJournalReadRequest(snapshot.JournalName, snapshot.InstanceId,
                snapshot.OwnerGeneration, snapshot.ContentRevision, offset), cancellationToken);

    internal Task<RuntimeJournalCatalog> GetCatalogAsync(CancellationToken cancellationToken)
        => ReadAsync<RuntimeJournalCatalog, int>(GrainReadKind.RuntimeJournalCatalog,
            GrainContracts.NoDtoMarker, cancellationToken);

    internal async Task<RuntimeJournalMutationResult> MutateAsync(RuntimeJournalMutation mutation,
        CancellationToken cancellationToken)
    {
        var payload = mutation;
        var commandId = Guid.NewGuid();
        var reply = await SendCommandAsync(payload, commandId, cancellationToken);
        if (reply.Error == ErrorCode.UnknownWriteOutcome
            && journalOptions.Value.UncertaintyRetryCount > RuntimeJournalStoragePolicy.NoUncertaintyRetryCount
            && !cancellationToken.IsCancellationRequested)
        {
            reply = await SendCommandAsync(payload, commandId, cancellationToken);
        }

        return (Decode<RuntimeJournalMutationResult>(reply, allowNull: false)).Value
            ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);
    }

    private async Task<T> ReadAsync<T, TPayload>(GrainReadKind kind, TPayload payload,
        CancellationToken cancellationToken) where T : class where TPayload : notnull
        => await ReadCoreAsync<T, TPayload>(kind, payload, allowNull: false, cancellationToken: cancellationToken)
            ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);

    private Task<T?> ReadNullableAsync<T, TPayload>(GrainReadKind kind, TPayload payload,
        CancellationToken cancellationToken) where T : class where TPayload : notnull
        => ReadCoreAsync<T, TPayload>(kind, payload, allowNull: true, cancellationToken: cancellationToken);

    private async Task<T?> ReadCoreAsync<T, TPayload>(GrainReadKind kind, TPayload payload,
        bool allowNull, CancellationToken cancellationToken) where T : class where TPayload : notnull
    {
        var token = cancellationToken;
        var codec = await PrepareAsync(token);
        var requestId = Guid.NewGuid();
        var issued = codec.CreateRuntimeJournalRead(requestId, kind, payload);
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        using var identity = GrainRequestIdentityScope.ForRuntimeJournal(services, requestId,
            Guid.Empty, connectionId, token);
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            createStream: streamToken => actor.ExecuteStreamAsync(issued, streamToken), requestId: requestId, cancellationToken: token);
        return (Decode<T>(reply, allowNull)).Value;
    }

    private async Task<GrainOperationReply> SendCommandAsync(RuntimeJournalMutation payload, Guid commandId,
        CancellationToken cancellationToken)
    {
        var token = cancellationToken;
        var codec = await PrepareAsync(token);
        var requestId = Guid.NewGuid();
        var issued = codec.CreateRuntimeJournalCommand(requestId, RuntimeJournalIdentity.ProtectedPrincipalId, commandId, payload);
        var connectionId = ConnectionExecutionIdentity.Resolve(services);
        using var identity = GrainRequestIdentityScope.ForRuntimeJournal(services, requestId, commandId, connectionId, token);
        var actor = grains.GetGrain<IConnectionGrain>(connectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            createStream: streamToken => actor.ExecuteStreamAsync(issued, streamToken), requestId: requestId, cancellationToken: token);
    }

    private async Task<GrainRequestCodec> PrepareAsync(CancellationToken cancellationToken)
    {
        await admission.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return codecFactory();
    }

    private static (bool HasValue, T? Value) Decode<T>(GrainOperationReply reply, bool allowNull = false) where T : class
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? RuntimeJournalStoragePolicy.RequestFailed);
        }

        var /* KLD0041: Inspect the current generated GrainValue slot for an absent or correctly typed runtime journal response. */ value = (GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */);
        if (value is null && allowNull)
        {
            return (false, default(T?));
        }

        return (true, ((T?)((value as T /* KLD0042: Validate the current generated GrainValue slot as T? before accepting this concrete RPC response. */) ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply))));
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
