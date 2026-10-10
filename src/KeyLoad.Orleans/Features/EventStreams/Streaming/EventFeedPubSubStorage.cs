using global::Orleans.Serialization;
using global::Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class EventFeedPubSubStorage(IServiceProvider originalServices,
    IOptions<DatabaseLimits> options, IOptions<GrainRoutingOptions> routing) : IGrainStorage, IDisposable
{
    private const long EmptyBytes = 0;
    private const int NewEntry = 1;
    private const int ExistingEntry = 0;
    private readonly Lock gate = new();
    private readonly Dictionary<GrainId, EventFeedPubSubRow> entries = new();
    private long retainedBytes;
    private bool closed;

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        => ReadStateAsync(stateName, grainId, state, CancellationToken.None);

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        EventFeedPubSubIdentity.Require(stateName, grainId);
        lock (gate)
        {
            RequireOpen(cancellationToken);
            if (entries.TryGetValue(grainId, out var row))
            {
                RequireType<T>(row);
                state.State = originalServices.GetRequiredService<Serializer<T>>().Deserialize(row.Value);
                state.ETag = row.ETag;
                state.RecordExists = true;
            }
            else
            { state.ETag = null; state.RecordExists = false; }
        }
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        => WriteStateAsync(stateName, grainId, state, CancellationToken.None);

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        EventFeedPubSubIdentity.Require(stateName, grainId);
        lock (gate)
        {
            RequireOpen(cancellationToken);
            entries.TryGetValue(grainId, out var before);
            RequireRevision(state.ETag, before);
            if (before is not null)
            { RequireType<T>(before); }
            var row = EventFeedPubSubRowEncoding.Create(originalServices, grainId, state.State!,
                options, routing, cancellationToken);
            var nextBytes = checked(retainedBytes - (before?.EncodedBytes ?? EmptyBytes) + row.EncodedBytes);
            var nextCount = checked(entries.Count + (before is null ? NewEntry : ExistingEntry));
            if (nextBytes > options.Value.MaxQueryReadBytes || nextCount > options.Value.MaxResults)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, EventFeedPubSubProtocol.Capacity); }
            cancellationToken.ThrowIfCancellationRequested();
            entries[row.Key] = row;
            retainedBytes = nextBytes;
            state.ETag = row.ETag;
            state.RecordExists = true;
        }
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        => ClearStateAsync(stateName, grainId, state, CancellationToken.None);

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        EventFeedPubSubIdentity.Require(stateName, grainId);
        lock (gate)
        {
            RequireOpen(cancellationToken);
            entries.TryGetValue(grainId, out var before);
            RequireRevision(state.ETag, before);
            if (before is not null)
            {
                RequireType<T>(before);
                var nextBytes = checked(retainedBytes - before.EncodedBytes);
                entries.Remove(grainId);
                retainedBytes = nextBytes;
            }
            state.ETag = null;
            state.RecordExists = false;
        }
        return Task.CompletedTask;
    }

    private void RequireOpen(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(closed, this);
        options.Value.Validate();
    }

    private static void RequireRevision(string? expected, EventFeedPubSubRow? row)
    {
        if (expected != row?.ETag)
        { throw new InconsistentStateException(EventFeedPubSubProtocol.Conflict, row?.ETag, expected, storageException: null); }
    }

    private static void RequireType<T>(EventFeedPubSubRow row)
    {
        if (row.StateType != typeof(T))
        { throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            foreach (var row in entries.Values)
            { row.Value.AsSpan().Clear(); }
            entries.Clear();
            retainedBytes = EmptyBytes;
        }
    }
}
