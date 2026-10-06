#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Core;
using Orleans.Journaling;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Orleans;

/// <summary>Implements native storage operations against one captured journal generation.</summary>
internal sealed class RuntimeJournalStorage : IJournalStorage
{
    private readonly RuntimeJournalStorageState state;
    private readonly RuntimeJournalStorageOperations operations;

    internal RuntimeJournalStorage(RuntimeJournalClient client, JournalId journalId,
        Microsoft.Extensions.Options.IOptions<RuntimeJournalOptions> options, string format,
        Microsoft.Extensions.Options.IOptions<GrainRoutingOptions> routingOptions, SemaphoreSlim[] gates,
        int gateIndex)
    {
        state = new(client, journalId, options, format, routingOptions, gates, gateIndex);
        operations = new(client, state);
    }

    public bool IsCompactionRequested => state.Options.Value.RequestCompaction;

    public async ValueTask<bool> CreateIfNotExistsAsync(IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await state.EnterAsync(cancellationToken).ConfigureAwait(false);
        var properties = RuntimeJournalStorageValidation.CopyProperties(metadata, state.Options.Value);
        var observed = await state.ObserveAsync(lease.Token).ConfigureAwait(false);
        if (observed is not null || state.Captured is not null)
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
        var snapshot = await state.ObserveAsync(lease.Token).ConfigureAwait(false);
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

        var properties = RuntimeJournalStorageValidation.CopyProperties(set, state.Options.Value);
        var removals = RuntimeJournalStorageValidation.CopyRemovals(remove, properties, state.Options.Value);
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

        if (snapshot.Length > state.Options.Value.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidPage);
        }

        var metadata = operations.Metadata(snapshot);
        using var buffer = new ArcBufferWriter();
        var offset = RuntimeJournalStoragePolicy.InitialReadOffset;
        while (true)
        {
            lease.Token.ThrowIfCancellationRequested();
            var page = await operations.ReadPageAsync(snapshot, offset, lease.Token).ConfigureAwait(false);
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
