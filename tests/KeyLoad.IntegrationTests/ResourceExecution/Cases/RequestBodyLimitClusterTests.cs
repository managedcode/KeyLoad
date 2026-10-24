using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests;

[NotInParallel]
internal sealed class RequestBodyLimitClusterTests
{
    private const string PartitionKey = "partition";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private const int MaximumBodyBytes = 1_024;

    [Test]
    public async Task OversizedDeclaredAndChunkedBodiesAreRejectedBeforeCommandsClaimTheirIds()
    {
        var fixture = new ClusterFixture(maximumBodyBytes: MaximumBodyBytes);
        using var timeout = McpCallerDeadline.Create();
        try
        {
            await fixture.InitializeAsync();
            using var http = fixture.App.CreateHttpClient("node1", "http");
            foreach (var chunked in new[] { false, true })
            {
                var id = Guid.NewGuid();
                var payload = new DeliveryCommand(id, new(new("tenant", "database", "orders", PartitionKey), "jobs"),
                    new string('t', 2_000), DeliveryAction.Ack);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/queues/delivery")
                {
                    Content = new ByteArrayContent(JsonDefaults.Serialize(payload))
                };
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
                using var control = new HttpRequestMessage(HttpMethod.Post, "/admin/dispatch?paused=false");
                control.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
                control.Headers.Add(CommandIdHeader, id.ToString());
                using var completed = await http.SendAsync(control, timeout.Token);
                completed.EnsureSuccessStatusCode();
                await Assert.That(await completed.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, timeout.Token)).IsTrue();
            }
        }
        finally { await fixture.DisposeAsync(); }
    }
}
