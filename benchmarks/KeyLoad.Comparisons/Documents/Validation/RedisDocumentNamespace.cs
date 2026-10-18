using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns bounded document namespace readback and final native deletion.</summary>
internal sealed class RedisDocumentNamespace(string prefix, int maximumIdentity, int capacity)
{
    private const string DocumentBatchReadCommand = "MGET";
    internal async IAsyncEnumerable<FoundDocument> ReadAsync(ConnectionMultiplexer actual,
        [EnumeratorCancellation] CancellationToken token, CommandFlags flags = CommandFlags.DemandMaster)
    {
        // Native SCAN may repeat keys. A bounded identity bitset deduplicates observations without retaining payloads.
        var present = new BitArray(maximumIdentity);
        var endpoint = actual.GetEndPoints(configuredOnly: true).Single();
        var server = actual.GetServer(endpoint);
        await foreach (var key in server.KeysAsync(pattern: prefix + DocumentProtocolText.EmptyText, pageSize: capacity, flags: flags)
            .WithCancellation(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            var id = key.ToString()[prefix.Length..];
            if (id.Length != DocumentMeasurementValues.DocumentIdentifierLength || id[DocumentMeasurementValues.NoObservedItems] != DocumentProtocolText.DocumentIdentityPrefix || !int.TryParse(id.AsSpan(DocumentMeasurementValues.SingleItemCount), NumberStyles.None,
                CultureInfo.InvariantCulture, out var ordinal) || ordinal < DocumentMeasurementValues.NoObservedItems || ordinal >= maximumIdentity
                || id != ScaledComparisonCorpus.Id(ordinal))
            {
                throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackUnexpectedNativeKey);
            }

            present[ordinal] = true;
        }
        var database = actual.GetDatabase();
        var identities = new List<string>(capacity);
        for (var ordinal = DocumentMeasurementValues.NoObservedItems; ordinal < present.Length; ordinal++)
        {
            if (!present[ordinal])
            {
                continue;
            }

            identities.Add(ScaledComparisonCorpus.Id(ordinal));
            if (identities.Count < capacity)
            {
                continue;
            }

            foreach (var value in await ReadBatchAsync().ConfigureAwait(false))
            {
                yield return value;
            }
        }
        if (identities.Count > DocumentMeasurementValues.NoObservedItems)
        {
            foreach (var value in await ReadBatchAsync().ConfigureAwait(false))
            {
                yield return value;
            }
        }

        async Task<FoundDocument[]> ReadBatchAsync()
        {
            token.ThrowIfCancellationRequested();
            var keys = identities.Select(id => (object)(prefix + id)).ToArray();
            // Await the original bounded native MGET, including when cancellation arrives during execution.
            // IServer binds MGET to this exact observed endpoint; IDatabase routing could choose another discovered replica.
            var values = (RedisResult[])(await server.ExecuteAsync(database.Database, DocumentBatchReadCommand, keys, flags).ConfigureAwait(false))!;
            token.ThrowIfCancellationRequested();
            if (values.Length != identities.Count || values.Any(value => value.IsNull))
            {
                throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackMissingNativeKey);
            }

            var result = identities.Select((id, index) => new FoundDocument(id, values[index].ToString())).ToArray();
            identities.Clear();
            return result;
        }
    }
    internal async Task CleanupAsync(ConnectionMultiplexer connection, TimeSpan timeout, TimeProvider timeProvider)
    {
        using var deadline = new CancellationTokenSource(timeout, timeProvider);
        var endpoint = connection.GetEndPoints(configuredOnly: true).Single();
        var server = connection.GetServer(endpoint);
        var database = connection.GetDatabase();
        var keys = new List<RedisKey>(capacity);
        await foreach (var key in server.KeysAsync(pattern: prefix + DocumentProtocolText.EmptyText, pageSize: capacity)
            .WithCancellation(deadline.Token).ConfigureAwait(false))
        {
            keys.Add(key);
            if (keys.Count < capacity)
            {
                continue;
            }

            await DeleteBatchAsync().ConfigureAwait(false);
        }
        if (keys.Count > DocumentMeasurementValues.NoObservedItems)
        {
            await DeleteBatchAsync().ConfigureAwait(false);
        }

        async Task DeleteBatchAsync()
        {
            deadline.Token.ThrowIfCancellationRequested();
            // The original bounded DEL always joins before cancellation can leave this namespace owner.
            _ = await database.KeyDeleteAsync(keys.ToArray(), CommandFlags.DemandMaster).ConfigureAwait(false);
            keys.Clear();
            deadline.Token.ThrowIfCancellationRequested();
        }
        await foreach (var _ in server.KeysAsync(pattern: prefix + DocumentProtocolText.EmptyText, pageSize: capacity)
            .WithCancellation(deadline.Token).ConfigureAwait(false))
        {
            throw new ComparisonFailureException(DocumentProtocolText.DocumentNativeCleanupIncomplete);
        }
    }
}
