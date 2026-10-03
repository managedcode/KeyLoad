using KeyLoad.Client;
using KeyLoad.UnitTests.Features.ClientApi;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-005: real mid-body cancellation preserves SQL write uncertainty.</summary>
internal sealed class SqlClientCancellationTests
{
    private const string Select = "/* client */ SELECT * FROM orders";
    private const string Call = "/* client */ CALL keyload_documents_commit(@arguments)";
    private const string ApiKey = "sql-body-cancel-test-key";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string Partition = "one";
    private const string JsonType = "application/json";
    private const string PartialReply = "{";
    private const string SuccessReply = "{}";
    private const int TimeoutSeconds = 5;

    [Test]
    [Arguments(Select, ErrorCode.Cancelled)]
    [Arguments(Call, ErrorCode.UnknownWriteOutcome)]
    public async Task AC_SQLC_005_ActualPartialBodyCancellationAllowsAFollowingRequest(string sql, ErrorCode expected)
    {
        var firstChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task? originalResponse = null;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(context =>
        {
            context.Response.ContentType = JsonType;
            if (Interlocked.Increment(ref calls) == 1)
            {
                var response = FirstResponseAsync(context, firstChunk, stopped);
                Volatile.Write(ref originalResponse, response);
                return response;
            }
            return context.Response.WriteAsync(SuccessReply, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource();
        var client = new KeyLoadClient(server.Client, ApiKey);
        var request = new SqlOperationRequest(new(Tenant, Database, Domain, Partition), sql);
        var pending = client.ExecuteSqlAsync(request, cancellation.Token);
        Exception? failure = null;
        try
        {
            await firstChunk.Task.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds));
            await Assert.That(pending.IsCompleted).IsFalse();
            await cancellation.CancelAsync();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds));
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
            using var nextCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            var next = await client.ExecuteSqlAsync(request, nextCancellation.Token);
            await Assert.That(next.IsSuccess).IsTrue();
            await Assert.That(calls).IsEqualTo(2);
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            await SqlClientCancellationCleanup.DrainAsync(failure, cancellation, pending,
                () => Volatile.Read(ref originalResponse), stopped.Task, TimeSpan.FromSeconds(TimeoutSeconds));
        }
    }

    private static async Task FirstResponseAsync(HttpContext context, TaskCompletionSource firstChunk,
        TaskCompletionSource stopped)
    {
        try
        {
            await context.Response.WriteAsync(PartialReply, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            firstChunk.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { }
        catch (Exception error)
        {
            firstChunk.TrySetException(error);
            stopped.TrySetException(error);
            throw;
        }
        finally
        { stopped.TrySetResult(); }
    }
}
