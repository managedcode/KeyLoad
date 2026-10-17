using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisSessionQueryExecution
{
    private const string SessionIndexUnavailable = "SelectedRedisNativeProfileHasNoSecondaryIndexLifecycle";
    private const string SessionPrimaryUnavailable = "SessionNativeRedisPrimaryUnavailable";
    private const string SessionReadCommand = "EVAL_RO";
    private const int SessionKeyCount = 1;
    private const int SessionRangeWidth = 1;

    private const string SessionQuery = """
        local payload = redis.call('GET', KEYS[1])
        if not payload then return false end
        local document = cjson.decode(payload)
        if document.id ~= ARGV[1] or document.number < tonumber(ARGV[2]) or document.number >= tonumber(ARGV[3]) then
            return false
        end
        return payload
        """;
    /// <inheritdoc/>
    internal static Task BuildIndexesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new BenchmarkSessionNativeCapabilityException(SessionIndexUnavailable));
    }

    /// <inheritdoc/>
    internal static async Task QueryAsync(ConnectionMultiplexer? connection, string prefix, int ordinal,
        int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (connection is null)
        {
            throw new InvalidOperationException(SessionPrimaryUnavailable);
        }
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        // Join the original native request, matching this target's existing document-session cancellation contract.
        var result = await connection.GetDatabase().ExecuteAsync(SessionReadCommand,
            new object[] { SessionQuery, SessionKeyCount, prefix + expected.Id, expected.Id, expected.Number, expected.Number + SessionRangeWidth },
            CommandFlags.DemandMaster).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        BenchmarkSessionQueryContract.Require(result.IsNull ? null : new(expected.Id, result.ToString()), expected);
    }
}
