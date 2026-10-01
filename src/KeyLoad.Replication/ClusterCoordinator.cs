using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using DotNext.Net.Cluster.Consensus.Raft;
using KeyLoad.Core;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.Replication;

public sealed class ClusterCoordinator : ICommitCoordinator, IHostedService
{
    private readonly IRaftCluster cluster;
    private readonly DatabaseEngine database;
    private readonly HttpClient forwarding;
    private readonly Channel<Pending> commands;
    private readonly CancellationTokenSource lifetime = new();
    private Task? worker;
    public ClusterCoordinator(IRaftCluster cluster, DatabaseEngine database, PeerSecurity security)
    {
        this.cluster = cluster; this.database = database;
        database.Durability = DurabilityProfile.QuorumProcessDurable;
        forwarding = new HttpClient(security.CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        commands = Channel.CreateBounded<Pending>(new BoundedChannelOptions(database.Limits.WriterQueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }
    public async Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id == Guid.Empty || System.Text.Encoding.UTF8.GetByteCount(payloadJson) > database.Limits.MaxBatchBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The command ID or byte budget is invalid.");
        var pending = new Pending(new(id, kind, principalId, DateTimeOffset.UtcNow, payloadJson), new(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!commands.Writer.TryWrite(pending)) throw Errors.Fail(ErrorCode.ResourceExhausted, "The partition writer queue is full.");
        try { return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, "The response was interrupted. Query or retry the same command ID."); }
    }
    public async Task<OperationResult> AcceptForwardedAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        if (cluster.LeadershipToken.IsCancellationRequested) throw Errors.Fail(ErrorCode.OwnershipLost, "This node is not the current leader.");
        return await SubmitAsync(operation.Kind, operation.Id, operation.PrincipalId, operation.PayloadJson, cancellationToken).ConfigureAwait(false);
    }
    public async Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!cluster.LeadershipToken.IsCancellationRequested)
            {
                await LeaderReadBarrierAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            if (cluster.Leader?.EndPoint is not UriEndPoint endpoint)
                throw Errors.Fail(ErrorCode.OwnershipLost, "The cluster has no known leader.");
            using var response = await forwarding.GetAsync(new Uri(endpoint.Uri, "/internal/read-barrier"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw Errors.Fail(ErrorCode.OwnershipLost, "The leader quorum read barrier is unavailable.");
            var barrier = await response.Content.ReadFromJsonAsync<ReadBarrierReceipt>(JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            if (barrier is null || barrier.Incarnation != database.Store.Identity.Incarnation || barrier.Position < 0)
                throw Errors.Fail(ErrorCode.OwnershipLost, "The leader read barrier scope is invalid.");
            await cluster.AuditTrail.WaitForApplyAsync(barrier.Position, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is QuorumUnreachableException or NotLeaderException or HttpRequestException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.OwnershipLost, "A quorum read barrier is unavailable."); }
    }
    public async Task<ReadBarrierReceipt> LeaderReadBarrierAsync(CancellationToken cancellationToken)
    {
        var leadership = cluster.LeadershipToken;
        if (leadership.IsCancellationRequested) throw Errors.Fail(ErrorCode.OwnershipLost, "This node is not the current leader.");
        var term = cluster.AuditTrail.Term;
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(leadership, cancellationToken);
        try { await cluster.ApplyReadBarrierAsync(ReadBarrierType.Strong, scope.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is QuorumUnreachableException or NotLeaderException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.OwnershipLost, "The leader quorum read barrier is unavailable."); }
        if (leadership.IsCancellationRequested || cluster.AuditTrail.Term != term)
            throw Errors.Fail(ErrorCode.OwnershipLost, "Leadership changed during the read barrier.");
        return new(database.Store.Identity.Incarnation, cluster.AuditTrail.LastCommittedEntryIndex, term);
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        worker = RunAsync(lifetime.Token);
        return Task.CompletedTask;
    }
    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var pending in commands.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    OperationResult result;
                    if (cluster.LeadershipToken.IsCancellationRequested)
                    {
                        if (cluster.Leader?.EndPoint is not UriEndPoint endpoint)
                            throw Errors.Fail(ErrorCode.OwnershipLost, "The cluster has no known leader.");
                        var address = new Uri(endpoint.Uri, "/internal/commands");
                        using var response = await forwarding.PostAsJsonAsync(address, pending.Operation, JsonDefaults.Options, deadline.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw Errors.Fail(ErrorCode.UnknownWriteOutcome, "The leader response was unavailable. Retry the same command ID.");
                        result = await response.Content.ReadFromJsonAsync<OperationResult>(JsonDefaults.Options, deadline.Token).ConfigureAwait(false)
                            ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, "The leader returned no outcome.");
                    }
                    else
                    {
                        // The leader chooses evaluation time after queue admission. The state machine owns precondition decisions.
                        var operation = pending.Operation with { EvaluatedAt = DateTimeOffset.UtcNow };
                        await cluster.ReplicateAsync(JsonDefaults.Serialize(operation).AsMemory(), token: deadline.Token).ConfigureAwait(false);
                        result = database.ResolveOutcome(operation);
                    }
                    pending.Completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    pending.Completion.TrySetException(exception is KeyLoadException ? exception :
                        Errors.Fail(ErrorCode.UnknownWriteOutcome, "Replication was interrupted. Query or retry the same command ID."));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            while (commands.Reader.TryRead(out var pending)) pending.Completion.TrySetException(
                Errors.Fail(ErrorCode.UnknownWriteOutcome, "The node stopped before returning a command outcome."));
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        commands.Writer.TryComplete();
        lifetime.Cancel();
        if (worker is not null) await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        forwarding.Dispose();
    }
    private sealed record Pending(ReplicatedOperation Operation, TaskCompletionSource<OperationResult> Completion);
}

public sealed record ReadBarrierReceipt(Guid Incarnation, long Position, long Term);
