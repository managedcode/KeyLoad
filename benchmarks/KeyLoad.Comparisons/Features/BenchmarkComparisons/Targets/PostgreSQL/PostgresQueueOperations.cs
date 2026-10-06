using System.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresQueueOperations
{

    private const string EnqueueSql = "INSERT INTO queue(id,body) VALUES ($1,$2)";
    private const string ClaimSql = """
        WITH candidate AS (SELECT id FROM queue WHERE state='ready' OR (state='leased' AND lease_until < now())
            ORDER BY id FOR UPDATE SKIP LOCKED LIMIT 1)
        UPDATE queue q SET state='leased',lease_owner=$1,lease_until=now()+$2::interval,attempts=attempts+1
        FROM candidate c WHERE q.id=c.id RETURNING q.id,q.body::text
        """;
    private const string AcknowledgeSql = "UPDATE queue SET state='acked',lease_until=NULL WHERE id=$1 AND lease_owner=$2 AND state='leased' AND lease_until>now()";
    private const string LeaseFailure = "PostgresLeaseLost";

    internal static async Task<OperationResult> ExecuteAsync(NpgsqlConnection connection, BenchmarkDocument document,
        IOptions<ComparisonLifecycleOptions> lifecycleOptions, CancellationToken cancellationToken)
    {
        var lifecycle = lifecycleOptions.Value;
        var begin = Stopwatch.GetTimestamp();
        var enqueued = await EnqueueAsync(connection, document, cancellationToken);
        var lease = Guid.NewGuid();
        var (message, received) = await ReceiveAsync(connection: connection, lease: lease, cancellationToken: cancellationToken,
            claimPollInterval: lifecycle.QueueClaimPollInterval, leaseDuration: lifecycle.QueueLeaseDuration);
        await AcknowledgeAsync(connection, message, lease, cancellationToken);
        return new(Message: message, Queue: new(Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds,
            Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds,
            Stopwatch.GetElapsedTime(received).TotalMilliseconds));
    }

    private static async Task<long> EnqueueAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EnqueueSql;
            command.Parameters.AddWithValue(document.Id);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return Stopwatch.GetTimestamp();
    }

    private static async Task<(FoundDocument Message, long ReceivedAt)> ReceiveAsync(NpgsqlConnection connection, Guid lease,
        TimeSpan claimPollInterval, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        FoundDocument? message = null;
        while (message is null)
        {
            await using var claim = connection.CreateCommand();
            claim.CommandText = ClaimSql;
            claim.Parameters.AddWithValue(lease);
            claim.Parameters.AddWithValue(leaseDuration);
            message = await ReadClaimAsync(claim, cancellationToken);
            if (message is null)
            {
                await Task.Delay(claimPollInterval, cancellationToken);
            }
        }

        return (message, Stopwatch.GetTimestamp());
    }

    private static async Task<FoundDocument?> ReadClaimAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new(reader.GetString(FirstColumnIndex), reader.GetString(SecondColumnIndex));
    }

    private static async Task AcknowledgeAsync(NpgsqlConnection connection, FoundDocument message, Guid lease,
        CancellationToken cancellationToken)
    {
        const int SingleItemCount = 1;

        await using var command = connection.CreateCommand();
        command.CommandText = AcknowledgeSql;
        command.Parameters.AddWithValue(message.Id);
        command.Parameters.AddWithValue(lease);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != SingleItemCount)
        {
            throw new ComparisonFailureException(LeaseFailure);
        }
    }
}
