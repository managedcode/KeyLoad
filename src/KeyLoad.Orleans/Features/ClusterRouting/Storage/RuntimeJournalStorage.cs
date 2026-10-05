#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Core;
using Orleans.Journaling;
using Orleans.Storage;

namespace KeyLoad.Orleans;

/// <summary>Implements native storage operations against one captured journal generation.</summary>
internal sealed class RuntimeJournalStorage : IJournalStorage
{
    private readonly RuntimeJournalStorageState state;
    private readonly RuntimeJournalStorageOperations operations;

    internal RuntimeJournalStorage(RuntimeJournalClient client, JournalId journalId, RuntimeJournalOptions options,
        string format, Microsoft.Extensions.Options.IOptions<GrainRoutingOptions> routingOptions)
    {
        state = new(client, journalId, options with { }, format, routingOptions);
        operations = new(client, state);
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

        operations.EnsureAvailable();
        var result = await operations.CreateAsync(properties, lease.Token).ConfigureAwait(false);
        state.CaptureInitial(result.Snapshot);
        return result.Applied;
    }

    public async ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        return snapshot is null ? null : operations.Metadata(snapshot);
    }

    public async ValueTask<IJournalMetadata?> UpdateMetadataAsync(IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null, string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        operations.EnsureAvailable();
        var current = await state.CaptureAsync(lease.Token).ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        var properties = RuntimeJournalStorageValidation.CopyProperties(set, state.Options);
        var removals = RuntimeJournalStorageValidation.CopyRemovals(remove, properties, state.Options);
        return await operations.UpdateMetadataAsync(current, properties, removals, expectedETag, lease.Token)
            .ConfigureAwait(false);
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

        var metadata = operations.Metadata(snapshot);
        var offset = 0L;
        while (true)
        {
            lease.Token.ThrowIfCancellationRequested();
            var page = await operations.ReadPageAsync(snapshot, offset, lease.Token).ConfigureAwait(false);
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

        var result = await operations.MutateBodyAsync(
            RuntimeJournalStorageOperations.BodyMutation(RuntimeJournalAction.Delete, current, ReadOnlyMemory<byte>.Empty),
            lease.Token).ConfigureAwait(false);
        if (result.Applied)
        {
            state.Retire();
        }
    }

    private async ValueTask WriteBodyAsync(ReadOnlySequence<byte> value, RuntimeJournalAction action,
        CancellationToken cancellationToken)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        await operations.WriteBodyAsync(value, action, lease.Token).ConfigureAwait(false);
    }
}
#pragma warning restore ORLEANSEXP005
