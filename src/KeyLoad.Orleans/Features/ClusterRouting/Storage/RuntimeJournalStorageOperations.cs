#pragma warning disable ORLEANSEXP005
using System.Buffers;
using System.Collections.Immutable;
using Orleans.Journaling;
using Orleans.Storage;

namespace KeyLoad.Orleans;

/// <summary>Applies bounded native journal calls while preserving each storage handle's captured fence.</summary>
internal sealed class RuntimeJournalStorageOperations(RuntimeJournalClient client, RuntimeJournalStorageState state)
{
    internal async Task<RuntimeJournalMutationResult> CreateAsync(IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken)
    {
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.Create, state.JournalId.Value, Guid.NewGuid(),
            RuntimeJournalStoragePolicy.InitialOwnerGeneration, RuntimeJournalStoragePolicy.InitialContentRevision,
            null, ReadOnlyMemory<byte>.Empty, properties.ToDictionary(StringComparer.Ordinal), []);
        return await client.MutateAsync(mutation, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IJournalMetadata?> UpdateMetadataAsync(RuntimeJournalSnapshot current,
        ImmutableDictionary<string, string> properties, ImmutableArray<string> removals, string? expectedETag,
        CancellationToken cancellationToken)
    {
        var mutation = new RuntimeJournalMutation(RuntimeJournalAction.UpdateMetadata, current.JournalName,
            current.InstanceId, current.OwnerGeneration, current.ContentRevision, expectedETag,
            ReadOnlyMemory<byte>.Empty, properties.ToDictionary(StringComparer.Ordinal), removals);
        try
        {
            var result = await client.MutateAsync(mutation, cancellationToken).ConfigureAwait(false);
            state.AdvanceAfterMetadataWrite(current, result.Snapshot);
            return result.Snapshot is null ? null : Metadata(result.Snapshot);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Conflict or ErrorCode.NotFound)
        {
            return null;
        }
    }

    internal async Task<RuntimeJournalPage> ReadPageAsync(RuntimeJournalSnapshot snapshot, long offset,
        CancellationToken cancellationToken)
    {
        RuntimeJournalPage page;
        try
        {
            page = await client.ReadPageAsync(snapshot, offset, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Conflict or ErrorCode.NotFound)
        {
            throw new InconsistentStateException(RuntimeJournalStoragePolicy.Inconsistent, error);
        }
        if (page.Data.Length > state.Options.Value.ChunkBytes || offset > snapshot.Length
            || page.Data.Length > snapshot.Length - offset || (!page.IsCompleted && page.Data.IsEmpty)
            || (page.IsCompleted && offset + page.Data.Length != snapshot.Length))
        {
            throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidPage);
        }
        return page;
    }

    internal async ValueTask WriteBodyAsync(ReadOnlySequence<byte> value, RuntimeJournalAction action,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (value.Length > state.Options.Value.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalStoragePolicy.InvalidLimits);
        }

        var current = await state.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            var created = await CreateAsync(ImmutableDictionary<string, string>.Empty, cancellationToken).ConfigureAwait(false);
            current = created.Snapshot ?? throw Errors.Fail(ErrorCode.Corruption, RuntimeJournalStoragePolicy.InvalidReply);
            state.CaptureInitial(current);
        }

        if (action == RuntimeJournalAction.Append
            && checked(current.Length + value.Length) > state.Options.Value.MaximumJournalBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RuntimeJournalStoragePolicy.InvalidLimits);
        }

        var data = value.ToArray();
        var result = await MutateBodyAsync(BodyMutation(action, current, data), cancellationToken).ConfigureAwait(false);
        if (result.Applied)
        {
            state.AdvanceAfterBodyWrite(current, result.Snapshot);
        }
    }

    internal async Task<RuntimeJournalMutationResult> MutateBodyAsync(RuntimeJournalMutation mutation,
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

    internal IJournalMetadata Metadata(RuntimeJournalSnapshot snapshot)
        => new JournalMetadata(state.Format, snapshot.MetadataETag, snapshot.Properties);

    internal void EnsureAvailable()
    {
        if (state.IsRetired)
        {
            throw new InconsistentStateException(RuntimeJournalStoragePolicy.Inconsistent);
        }
    }

    internal static RuntimeJournalMutation BodyMutation(RuntimeJournalAction action, RuntimeJournalSnapshot snapshot,
        ReadOnlyMemory<byte> data)
        => new(action, snapshot.JournalName, snapshot.InstanceId, snapshot.OwnerGeneration,
            snapshot.ContentRevision, null, data, new(StringComparer.Ordinal), []);
}
#pragma warning restore ORLEANSEXP005
