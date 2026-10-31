using global::Orleans.Serialization;
using global::Orleans.Serialization.Serializers;
using global::Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal sealed class EventFeedPubSubStorage(IServiceProvider originalServices) : IGrainStorage, IDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<GrainId, EventFeedPubSubRow> entries = new();
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
            {
                var replacement = CreateState<T>();
                cancellationToken.ThrowIfCancellationRequested();
                state.State = replacement;
                state.ETag = null;
                state.RecordExists = false;
            }
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
            {
                RequireType<T>(before);
            }
            var row = EventFeedPubSubRowEncoding.Create(originalServices, grainId, state.State!);
            cancellationToken.ThrowIfCancellationRequested();
            entries[row.Key] = row;
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
            }
            var replacement = CreateState<T>();
            cancellationToken.ThrowIfCancellationRequested();
            if (before is not null)
            {
                entries.Remove(grainId);
            }
            state.State = replacement;
            state.ETag = null;
            state.RecordExists = false;
        }
        return Task.CompletedTask;
    }

    private T CreateState<T>()
        => originalServices.GetRequiredService<IActivatorProvider>().GetActivator<T>().Create();

    private void RequireOpen(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(closed, this);
    }

    private static void RequireRevision(string? expected, EventFeedPubSubRow? row)
    {
        if (expected != row?.ETag)
        {
            throw new InconsistentStateException(EventFeedPubSubProtocol.Conflict, row?.ETag, expected, storageException: null);
        }
    }

    private static void RequireType<T>(EventFeedPubSubRow row)
    {
        if (row.StateType != typeof(T))
        {
            throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            foreach (var row in entries.Values)
            {
                row.Value.AsSpan().Clear();
            }
            entries.Clear();
        }
    }
}
