#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Core;
using Orleans.Journaling;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Clustering;

/// <summary>Implements execution storage operations against one captured journal generation.</summary>
internal sealed class RuntimeJournalStorage : IJournalStorage
{
    private readonly RuntimeJournalStorageState state;
    private readonly RuntimeJournalStorageOperations operations;

    internal RuntimeJournalStorage(RuntimeJournalClient client, JournalId journalId,
        Microsoft.Extensions.Options.IOptions<RuntimeJournalOptions> options, string format)
    {
        state = new(client, journalId, options, format);
        operations = new(client, state);
    }

    public bool IsCompactionRequested => state.Options.Value.RequestCompaction;

    public async ValueTask<bool> CreateIfNotExistsAsync(IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var properties = RuntimeJournalStorageValidation.CopyProperties(metadata, state.Options.Value);
        var observed = (await state.ObserveAsync(cancellationToken)).Value;
        if (observed is not null || state.Captured is not null)
        {
            return false;
        }

        operations.EnsureAvailable();
        var result = await operations.CreateAsync(properties, cancellationToken);
        state.CaptureInitial(result.Snapshot);
        return result.Applied;
    }

    public async ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = (await state.ObserveAsync(cancellationToken)).Value;
        return snapshot is null ? null : operations.Metadata(snapshot);
    }

    public async ValueTask<IJournalMetadata?> UpdateMetadataAsync(IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null, string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        operations.EnsureAvailable();
        var current = (await state.CaptureAsync(cancellationToken)).Value;
        if (current is null)
        {
            return null;
        }

        var properties = RuntimeJournalStorageValidation.CopyProperties(set, state.Options.Value);
        var removals = RuntimeJournalStorageValidation.CopyRemovals(remove, properties, state.Options.Value);
        return (await operations.UpdateMetadataAsync(current, properties, removals, expectedETag, cancellationToken)).Value;
    }

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer) /* KLD0041: BCL null validation accepts the consumer IJournalStorageConsumer reference through its object parameter. */;
        var snapshot = (await state.CaptureAsync(cancellationToken)).Value;
        if (snapshot is null)
        {
            consumer.Complete(JournalMetadata.Empty);
            return;
        }

        if (snapshot.Length > state.Options.Value.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidPage);
        }

        var metadata = operations.Metadata(snapshot);
        using var buffer = new ArcBufferWriter();
        var offset = RuntimeJournalStoragePolicy.InitialReadOffset;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await operations.ReadPageAsync(snapshot, offset, cancellationToken);
            if (!page.Data.IsEmpty)
            {
                buffer.Write(page.Data.Span);
                offset = checked(offset + page.Data.Length);
            }

            if (page.IsCompleted)
            {
                consumer.Read(new JournalBufferReader(buffer.Reader, isCompleted: true), metadata);
                if (buffer.Length > RuntimeJournalStoragePolicy.NoBufferedBytes)
                {
                    throw new InvalidOperationException(RuntimeJournalStoragePolicy.ConsumerLeftUnreadData);
                }

                return;
            }

            consumer.Read(new JournalBufferReader(buffer.Reader, isCompleted: false), metadata);
            if (buffer.Length > snapshot.Length || offset > snapshot.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidPage);
            }
        }
    }

    public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteBodyAsync(value, RuntimeJournalAction.Replace, cancellationToken);

    public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        => WriteBodyAsync(value, RuntimeJournalAction.Append, cancellationToken);

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        if (state.IsRetired)
        {
            return;
        }

        var current = (await state.CaptureAsync(cancellationToken)).Value;
        if (current is null)
        {
            return;
        }

        var result = await operations.MutateBodyAsync(
            RuntimeJournalStorageOperations.BodyMutation(RuntimeJournalAction.Delete, current, ReadOnlyMemory<byte>.Empty),
            cancellationToken);
        if (result.Applied)
        {
            state.Retire();
        }
    }

    private async ValueTask WriteBodyAsync(ReadOnlySequence<byte> value, RuntimeJournalAction action,
        CancellationToken cancellationToken)
    {
        await operations.WriteBodyAsync(value, action, cancellationToken);
    }
}
#pragma warning restore ORLEANSEXP005
