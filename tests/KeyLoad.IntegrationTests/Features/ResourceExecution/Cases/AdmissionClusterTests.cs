using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests;

[NotInParallel]
internal sealed class AdmissionClusterTests
{
    private const string PartitionKey = "partition";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";

    [Test]
    public async Task OversizedDeclaredAndChunkedBodiesAreRejectedBeforeCommandsClaimTheirIds()
    {
        var fixture = new ClusterFixture(new HttpAdmissionLimits { MaxBodyBytes = 1_024, MaxControlBodyBytes = 512 });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await fixture.InitializeAsync();
            using var http = fixture.App.CreateHttpClient("node1", "http");
            foreach (var chunked in new[] { false, true })
            {
                var id = Guid.NewGuid();
                var payload = new DeliveryCommand(id, new(new("tenant", "database", "orders", PartitionKey), "jobs"),
                    new string('t', 2_000), DeliveryAction.Ack);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/queues/delivery")
                { Content = new ByteArrayContent(JsonDefaults.Serialize(payload)) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
                if (chunked)
                {
                    request.Headers.TransferEncodingChunked = true;
                }
                using var rejected = await http.SendAsync(request, timeout.Token);
                await Assert.That(rejected.StatusCode).IsEqualTo(System.Net.HttpStatusCode.TooManyRequests);
                var problem = await rejected.Content.ReadFromJsonAsync<ManagedCode.Communication.Problem>(JsonDefaults.Options, timeout.Token);
                await Assert.That(problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
                using var control = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/dispatch?paused=false");
                control.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
                control.Headers.Add(CommandIdHeader, id.ToString());
                using var completed = await http.SendAsync(control, timeout.Token);
                completed.EnsureSuccessStatusCode();
                await Assert.That(await completed.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, timeout.Token)).IsTrue();
            }
            var status = await fixture.Client("node1").AdmissionStatusAsync(timeout.Token);
            await Assert.That(status.IsSuccess).IsTrue();
            await Assert.That(status.Value!.Http!.Limits.MaxControlBodyBytes).IsEqualTo(512);
            await Assert.That(status.Value.Http.Node.ControlCommands).IsEqualTo(0);
            await Assert.That(status.Value.Http.VerifiedScopes.ControlCommands).IsEqualTo(0);
        }
        finally { await fixture.DisposeAsync(); }
    }
    [Test]
    public async Task FullDataBudgetRejectsBeforeCommitWhileControlCommandsAndRf3RoutingStayAvailable()
    {
        var fixture = new ClusterFixture(commandBytes: 4_096);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await fixture.InitializeAsync();
            var partition = new PartitionRef("admission", "database", "orders", PartitionKey);
            foreach (var number in Enumerable.Range(1, 3))
            {
                var node = "node" + number;
                var client = fixture.Client(node);
                var commandId = Guid.NewGuid();
                var denied = await client.ConfigureResourceAsync(commandId, new(partition.TenantId, partition.DatabaseId,
                    new("orders", ResourceKind.Collection, "orders")), timeout.Token);
                await Assert.That(denied.IsSuccess).IsFalse();
                await Assert.That(denied.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
                var read = await client.GetAsync(new(partition, "orders", "unpublished"), timeout.Token);
                await Assert.That(read.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.NotFound));
                // An admission rejection did not claim the ID: a different control payload can now commit under it.
                using var http = fixture.App.CreateHttpClient(node, "http");
                using var control = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/dispatch?paused=false");
                control.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
                control.Headers.Add(CommandIdHeader, commandId.ToString());
                using var response = await http.SendAsync(control, timeout.Token);
                response.EnsureSuccessStatusCode();
                await Assert.That(await response.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, timeout.Token)).IsTrue();
                var admission = await client.AdmissionStatusAsync(timeout.Token);
                await Assert.That(admission.IsSuccess).IsTrue();
                await Assert.That(admission.Value!.Limits.MaxRetainedBytes).IsEqualTo(4_096);
                await Assert.That(admission.Value.Usage.Commands).IsEqualTo(0);
                await Assert.That(admission.Value.Usage.RetainedBytes).IsEqualTo(0);
                var status = await client.StatusAsync(timeout.Token);
                await Assert.That(status.IsSuccess).IsTrue();
                await Assert.That(status.Value!.RoutingReady).IsTrue();
                await Assert.That(status.Value.Voters).IsEqualTo(3);
            }
        }
        finally { await fixture.DisposeAsync(); }
    }
}
