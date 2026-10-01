using System.Diagnostics;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests;

[Collection("rf3")]
public sealed class ClusterTests(ClusterFixture fixture)
{
    private static T Success<T>(Result<T> result) { Assert.True(result.IsSuccess, result.Problem?.Detail); return result.Value!; }
    [Fact]
    public async Task ReplicatedAtomicBatchSurvivesLeaderProcessKillAndMinorityRejectsWrites()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
        var clients = Enumerable.Range(1, 3).Select(i => fixture.Client($"node{i}")).ToArray();
        var partition = new PartitionRef("integration", "database", "orders", Guid.NewGuid().ToString("N"));
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new("orders", ResourceKind.Collection, "orders") { Indexes = [new("number", ["/number"], true)] }), timeout.Token));
        Success(await clients[1].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, new("events", ResourceKind.StreamSet, "orders")), timeout.Token));
        Success(await clients[2].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, new("jobs", ResourceKind.WorkQueue, "orders")), timeout.Token));
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, partition, [new PutDocument("orders", "o1", "{\"number\":1}", 0),
            new AppendEvents("events", "o1", [new("e1", "Created", "{}")], ExpectedStreamRevision.NoStream), new EnqueueMessage("jobs", "m1", "{}")]);
        var committed = Success(await clients[2].CommitAsync(command, timeout.Token));
        Assert.Equal(DurabilityProfile.QuorumProcessDurable, committed.Durability);
        foreach (var client in clients)
        {
            Assert.Equal(1, Success(await client.GetAsync(new(partition, "orders", "o1"), timeout.Token))!.Revision);
            Assert.Single(Success(await client.ReadStreamAsync(new(new(partition, "events", "o1")), timeout.Token)).Events);
        }
        var statuses = await Task.WhenAll(clients.Select(c => c.StatusAsync(timeout.Token)));
        var status = statuses.Select(Success).ToArray();
        var leader = new Uri(status[0].Leader!);
        var leaderIndex = Enumerable.Range(0, 3).Single(index => fixture.App.GetEndpoint($"node{index + 1}", "http").Port == leader.Port);
        using (var process = Process.GetProcessById(status[leaderIndex].ProcessId)) { process.Kill(); await process.WaitForExitAsync(timeout.Token); }
        var survivors = Enumerable.Range(0, 3).Where(index => index != leaderIndex).ToArray();
        var surviving = clients[survivors[0]];
        await EventuallyAsync(async () => (await surviving.StatusAsync(timeout.Token)).IsSuccess, timeout.Token);
        var retried = await RetryDuringElectionAsync(() => surviving.CommitAsync(command, timeout.Token), timeout.Token);
        Assert.Equal(committed.Token, Success(retried).Token);
        Assert.Equal(1, Success(await surviving.GetAsync(new(partition, "orders", "o1"), timeout.Token))!.Revision);
        var receiveId = Guid.NewGuid();
        var delivery = Assert.Single(Success(await surviving.ReceiveAsync(new(receiveId, new(partition, "jobs")), timeout.Token)).Deliveries);
        var processingId = Guid.NewGuid();
        Success(await surviving.CommitProcessingAsync(new(processingId, new(partition, "jobs"), delivery.Token, "worker", 1,
            [new PatchDocument("orders", "o1", [new("/status", PatchKind.Set, "\"done\"")], 1)]), timeout.Token));
        Assert.Equal(MessageState.Acked, Success(await clients[survivors[1]].InspectAsync(new(new(partition, "jobs"), "m1"), timeout.Token))!.Metadata.State);
        var secondStatus = Success(await clients[survivors[1]].StatusAsync(timeout.Token));
        using (var process = Process.GetProcessById(secondStatus.ProcessId)) { process.Kill(); await process.WaitForExitAsync(timeout.Token); }
        await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
        var denied = await surviving.CommitAsync(new(Guid.NewGuid(), partition, [new PutDocument("orders", "minority", "{}")]), timeout.Token);
        Assert.True(denied.IsFailed);
        Assert.True((await surviving.GetAsync(new(partition, "orders", "o1"), timeout.Token)).IsFailed);
        foreach (var index in Enumerable.Range(0, 3).Where(i => i != survivors[0]))
            await fixture.App.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync($"node{index + 1}", "resource-start", timeout.Token);
        await EventuallyAsync(async () => (await surviving.StatusAsync(timeout.Token)).IsSuccess, timeout.Token);
        Assert.Null(Success(await surviving.GetAsync(new(partition, "orders", "minority"), timeout.Token)));
        foreach (var index in Enumerable.Range(0, 3))
        {
            await EventuallyAsync(async () => { var current = await clients[index].StatusAsync(timeout.Token);
                return current.IsSuccess && current.Value!.RoutingReady; }, timeout.Token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync($"node{index + 1}", timeout.Token);
        }
        }
        catch { await fixture.SaveFailureDiagnosticsAsync(); throw; }
    }
    [Fact]
    public async Task UnsignedPeerRequestsAndClientSuppliedPrincipalAreRejected()
    {
        using var http = fixture.App.CreateHttpClient("node1", "http");
        using var unsigned = await http.PostAsJsonAsync("/internal/commands", new ReplicatedOperation(Guid.NewGuid(), OperationKind.SetDispatch, "root", DateTimeOffset.UtcNow, "false"), JsonDefaults.Options, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unsigned.StatusCode);
        using var unsignedBarrier = await http.GetAsync("/internal/read-barrier", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unsignedBarrier.StatusCode);
        var invalid = fixture.Client("node1", "root.invalid-untrusted-credential-long-enough");
        Assert.True((await invalid.StatusAsync(TestContext.Current.CancellationToken)).IsFailed);
    }
    [Fact]
    public async Task FreshReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var clients = Enumerable.Range(1, 3).Select(i => fixture.Client($"node{i}")).ToArray();
            var partition = new PartitionRef("integration", "database", "snapshot", Guid.NewGuid().ToString("N"));
            var configureId = Guid.NewGuid();
            Success(await RetryDuringElectionAsync(() => clients[0].ConfigureResourceAsync(configureId, new(partition.TenantId, partition.DatabaseId,
                new("snapshots", ResourceKind.Collection, "snapshot")), timeout.Token), timeout.Token));
            CommandRequest? last = null; CommitReceipt? committed = null;
            for (var index = 0; index < 40; index++)
            {
                last = new(Guid.NewGuid(), partition, [new PutDocument("snapshots", "doc-" + index, "{\"n\":" + index + "}", 0)]);
                committed = Success(await RetryDuringElectionAsync(() => clients[index % clients.Length].CommitAsync(last, timeout.Token), timeout.Token));
            }
            foreach (var number in Enumerable.Range(1, 3))
                await EventuallyAsync(() => Task.FromResult(Directory.EnumerateFiles(Path.Combine(fixture.Root, $"node{number}", "snapshots"), "*-*").Any()), timeout.Token);
            var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(timeout.Token)))).Select(Success).ToArray();
            var leaderPort = new Uri(statuses[0].Leader!).Port;
            var follower = Enumerable.Range(0, 3).First(index => fixture.App.GetEndpoint($"node{index + 1}", "http").Port != leaderPort);
            using (var process = Process.GetProcessById(statuses[follower].ProcessId))
            { process.Kill(); await process.WaitForExitAsync(timeout.Token); }
            var directory = Path.Combine(fixture.Root, $"node{follower + 1}");
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try { Directory.Delete(directory, true); break; }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25, timeout.Token); }
            }
            await fixture.App.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync($"node{follower + 1}", "resource-start", timeout.Token);
            await EventuallyAsync(async () => { var status = await clients[follower].StatusAsync(timeout.Token);
                return status.IsSuccess && status.Value!.RoutingReady; }, timeout.Token);
            var recovered = Success(await clients[follower].StatusAsync(timeout.Token));
            Assert.NotEqual(statuses[follower].NodeId, recovered.NodeId);
            Assert.Equal(statuses[follower].Incarnation, recovered.Incarnation);
            Assert.Equal(1, Success(await clients[follower].GetAsync(new(partition, "snapshots", "doc-39"), timeout.Token))!.Revision);
            Assert.Equal(committed!.Token, Success(await RetryDuringElectionAsync(() => clients[follower].CommitAsync(last!, timeout.Token), timeout.Token)).Token);
            Assert.True(recovered.ReadGeneration > 0, "The empty replica must install a native snapshot rather than replay every historical command.");
        }
        catch { await fixture.SaveFailureDiagnosticsAsync(); throw; }
    }
    private static async Task EventuallyAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        while (!await predicate()) { cancellationToken.ThrowIfCancellationRequested(); await Task.Delay(250, cancellationToken); }
    }
    private static async Task<Result<T>> RetryDuringElectionAsync<T>(Func<Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await action();
            if (result.IsSuccess) return result;
            Assert.Contains(result.Problem?.ErrorCode, new[] { nameof(ErrorCode.UnknownWriteOutcome), nameof(ErrorCode.OwnershipLost) });
            await Task.Delay(250, cancellationToken);
        }
    }
}
