using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests;

[Collection("admission-rf3")]
public sealed class AdmissionClusterTests
{
    [Fact]
    public async Task FullDataBudgetRejectsBeforeCommitWhileControlCommandsAndRf3RoutingStayAvailable()
    {
        var fixture = new ClusterFixture(commandBytes: 4_096);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await fixture.InitializeAsync();
            var partition = new PartitionRef("admission", "database", "orders", "partition");
            foreach (var number in Enumerable.Range(1, 3))
            {
                var node = "node" + number; var client = fixture.Client(node); var commandId = Guid.NewGuid();
                var denied = await client.ConfigureResourceAsync(commandId, new(partition.TenantId, partition.DatabaseId,
                    new("orders", ResourceKind.Collection, "orders")), timeout.Token);
                Assert.False(denied.IsSuccess); Assert.Equal(nameof(ErrorCode.ResourceExhausted), denied.Problem!.ErrorCode);
                var read = await client.GetAsync(new(partition, "orders", "unpublished"), timeout.Token);
                Assert.Equal(nameof(ErrorCode.NotFound), read.Problem!.ErrorCode);
                // An admission rejection did not claim the ID: a different control payload can now commit under it.
                using var http = fixture.App.CreateHttpClient(node, "http");
                using var control = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/dispatch?paused=false");
                control.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
                control.Headers.Add("X-KeyLoad-Command-Id", commandId.ToString());
                using var response = await http.SendAsync(control, timeout.Token); response.EnsureSuccessStatusCode();
                Assert.True(await response.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, timeout.Token));
                var admission = await client.AdmissionStatusAsync(timeout.Token);
                Assert.True(admission.IsSuccess, admission.Problem?.Detail); Assert.Equal(4_096, admission.Value!.Limits.MaxRetainedBytes);
                Assert.Equal(0, admission.Value.Usage.Commands); Assert.Equal(0, admission.Value.Usage.RetainedBytes);
                var status = await client.StatusAsync(timeout.Token);
                Assert.True(status.IsSuccess, status.Problem?.Detail); Assert.True(status.Value!.RoutingReady); Assert.Equal(3, status.Value.Voters);
            }
        }
        finally { await fixture.DisposeAsync(); }
    }
}
[CollectionDefinition("admission-rf3", DisableParallelization = true)]
public sealed class AdmissionClusterCollection;
