using System.Buffers;
using System.Collections.Immutable;
using KeyLoad.Core;
using Orleans.Journaling;
using Orleans.Storage;

namespace KeyLoad.Orleans;

/// <summary>Implements native storage operations against one captured journal generation.</summary>
internal sealed class RuntimeJournalStorage : IJournalStorage
{
    private readonly RuntimeJournalClient client;
    private readonly RuntimeJournalStorageState state;

    internal RuntimeJournalStorage(RuntimeJournalClient client, JournalId journalId, RuntimeJournalOptions options,
        string format, Microsoft.Extensions.Options.IOptions<GrainRoutingOptions> routingOptions)
    {
        this.client = client;
        state = new(client, journalId, options with { }, format, routingOptions);
    }

    public bool IsCompactionRequested => state.Options.RequestCompaction;

    public async ValueTask<bool> CreateIfNotExistsAsync(IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        var properties = RuntimeJournalStorageValidation.CopyProperties(metadata, state.Options);
        if (await state.CaptureAsync(lease.Token).ConfigureAwait(false) is not null)
        {
            return false;
        }

        EnsureAvailable();
        var result = await CreateAsync(properties, lease.Token).ConfigureAwait(false);
        state.CaptureInitial(result.Snapshot);
        return result.Applied;
    }

    public async ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        return snapshot is null ? null : Metadata(snapshot);
    }

    public async ValueTask<IJournalMetadata?> UpdateMetadataAsync(IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null, string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        EnsureAvailable();
        var current = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        var properties = RuntimeJournalStorageValidation.CopyProperties(set, state.Options);
        var removals = RuntimeJournalStorageValidation.CopyRemovals(remove, properties, state.Options);
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.UpdateMetadata, current.JournalName,
            current.InstanceId, current.OwnerGeneration, current.ContentRevision, expectedETag,
            ReadOnlyMemory<byte>.Empty, properties.ToDictionary(StringComparer.Ordinal), removals);
        try
        {
            var result = await client.MutateAsync(mutation, lease.Token).ConfigureAwait(false);
            state.AdvanceAfterMetadataWrite(current, result.Snapshot);
            return result.Snapshot is null ? null : Metadata(result.Snapshot);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Conflict)
        {
            return null;
        }
    }

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        if (snapshot is null)
        {
            consumer.Complete(JournalMetadata.Empty);
            return;
        }

        var metadata = Metadata(snapshot);
        var offset = 0L;
        while (true)
        {
            lease.Token.ThrowIfCancellationRequested();
            var page = await ReadPageAsync(snapshot, offset, lease.Token).ConfigureAwait(false);
            ValidatePage(page, snapshot, offset);
            if (!page.Data.IsEmpty)
            {
                consumer.Read(page.Data, metadata, complete: false);
                offset = checked(offset + page.Data.Length);
            }

            if (page.IsCompleted)
            {
                consumer.Complete(metadata);
                return;
            }
        }
    }

    public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteBodyAsync(value, RuntimeJournalAction.Replace, cancellationToken);

    public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteBodyAsync(value, RuntimeJournalAction.Append, cancellationToken);

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (state.IsRetired)
        {
            return;
        }

        var current = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        var mutation = BodyMutation(RuntimeJournalAction.Delete, current, ReadOnlyMemory<byte>.Empty);
        var result = await MutateBodyAsync(mutation, lease.Token).ConfigureAwait(false);
        if (result.Applied)
        {
            state.Retire();
        }
    }

    private async ValueTask WriteBodyAsync(ReadOnlySequence<byte> value, RuntimeJournalAction action,
        CancellationToken cancellationToken)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        EnsureAvailable();
        if (value.Length > state.Options.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalStoragePolicy.InvalidLimits);
        }

        var current = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        if (current is null)
        {
            var created = await CreateAsync(ImmutableDictionary<string, string>.Empty, lease.Token).ConfigureAwait(false);
            current = created.Snapshot ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);
            state.CaptureInitial(current);
        }

        if (action == RuntimeJournalAction.Append
            && checked(current.Length + value.Length) > state.Options.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalStoragePolicy.InvalidLimits);
        }

        var data = value.ToArray();
        var result = await MutateBodyAsync(BodyMutation(action, current, data), lease.Token).ConfigureAwait(false);
        if (result.Applied)
        {
            state.AdvanceAfterBodyWrite(current, result.Snapshot);
        }
    }

    private async Task<RuntimeJournalMutationResult> MutateBodyAsync(RuntimeJournalMutation mutation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.MutateAsync(mutation, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Conflict or ErrorCode.NotFound)
        {
            throw new InconsistentStateException(RuntimeJournalStoragePolicy.Inconsistent, error);
        }
    }

    private Task<RuntimeJournalPage> ReadPageAsync(RuntimeJournalSnapshot snapshot, long offset,
        CancellationToken cancellationToken)
        => client.ReadPageAsync(snapshot, offset, cancellationToken);

    private async Task<RuntimeJournalMutationResult> CreateAsync(IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken)
    {
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.Create, state.JournalId.Value, Guid.NewGuid(),
            0, 0, null, ReadOnlyMemory<byte>.Empty, properties.ToDictionary(StringComparer.Ordinal), []);
        return await client.MutateAsync(mutation, cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeJournalMutation BodyMutation(RuntimeJournalAction action, RuntimeJournalSnapshot snapshot,
        ReadOnlyMemory<byte> data)
        => new(action, snapshot.JournalName, snapshot.InstanceId, snapshot.OwnerGeneration,
            snapshot.ContentRevision, null, data, new(StringComparer.Ordinal), []);

    private IJournalMetadata Metadata(RuntimeJournalSnapshot snapshot)
        => new JournalMetadata(state.Format, snapshot.MetadataETag, snapshot.Properties);

    private void ValidatePage(RuntimeJournalPage page, RuntimeJournalSnapshot snapshot, long offset)
    {
        if (page.Data.Length > state.Options.ChunkBytes || offset > snapshot.Length || page.Data.Length > snapshot.Length - offset
            || (!page.IsCompleted && page.Data.IsEmpty)
            || (page.IsCompleted && offset + page.Data.Length != snapshot.Length))
        {
            throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidPage);
        }
    }

    private void EnsureAvailable()
    {
        if (state.IsRetired)
        {
            throw new InconsistentStateException(RuntimeJournalStoragePolicy.Inconsistent);
        }
    }
}
