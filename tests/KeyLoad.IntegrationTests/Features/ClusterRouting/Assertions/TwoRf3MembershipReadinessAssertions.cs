using System.Net;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipReadinessAssertions
{
    private const string AdmissionDetail = "The physical shard catalog is not ready for public admission.";
    private const string ProblemType = "type";
    private const string ProblemTitle = "title";
    private const string ProblemStatus = "status";
    private const string ProblemDetail = "detail";
    private const string ProblemErrorCode = "errorCode";

    internal static async Task VerifyAllNodesAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { await VerifyNodeAsync(app, node, cancellationToken).ConfigureAwait(false); }
    }

    private static async Task VerifyNodeAsync(DistributedApplication app, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthSilo, 200, cancellationToken).ConfigureAwait(false);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthMembership, 200, cancellationToken).ConfigureAwait(false);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthReady, 503, cancellationToken).ConfigureAwait(false);
        var authority = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < TwoRf3MembershipProtocol.MembersPerGroup ? 200 : 503;
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthAuthority, authority, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task VerifyPublicCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        await VerifySdkCallsClosedAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
        await VerifyOfficialMcpCallsClosedAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySdkCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var client = new KeyLoadClient(http, adminKey, IntegrationClientOptions.Execution());
        var read = await client.QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        await RequireRejectedAsync(read.IsSuccess, read.Problem).ConfigureAwait(false);
        var partition = new PartitionRef("membership-stage1a", "database", "group-closure", node);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument("membership-probe", "must-not-dispatch", "{\"value\":1}", 0)]);
        var write = await client.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        await RequireRejectedAsync(write.IsSuccess, write.Problem).ConfigureAwait(false);
    }

    private static async Task VerifyOfficialMcpCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        McpOfficialClient? unexpected = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                unexpected = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken)
                    .ConfigureAwait(false);
            });
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(failure.Message.Contains(adminKey, StringComparison.Ordinal)).IsFalse();
            await RequireNativeAdmissionProblemAsync(failure).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (unexpected is { } owner)
        { await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireNativeAdmissionProblemAsync(HttpRequestException failure)
    {
        const string marker = "Response body: ";
        const int maximumBytes = 1024;
        await Assert.That(Encoding.UTF8.GetByteCount(failure.Message) <= maximumBytes).IsTrue();
        var start = failure.Message.IndexOf(marker, StringComparison.Ordinal);
        await Assert.That(start >= 0).IsTrue();
        using var document = JsonDocument.Parse(failure.Message[(start + marker.Length)..]);
        var body = document.RootElement;
        await Assert.That(body.ValueKind).IsEqualTo(JsonValueKind.Object);
        var fields = body.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(fields.Length).IsEqualTo(5);
        await Assert.That(new HashSet<string>(fields, StringComparer.Ordinal).SetEquals(
            [ProblemType, ProblemTitle, ProblemStatus, ProblemDetail, ProblemErrorCode])).IsTrue();
        await Assert.That(body.GetProperty(ProblemType).GetString()).IsEqualTo("urn:keyload:error:OwnershipLost");
        await Assert.That(body.GetProperty(ProblemTitle).GetString()).IsEqualTo("OwnershipLost");
        await Assert.That(body.GetProperty(ProblemStatus).GetInt32()).IsEqualTo(503);
        await Assert.That(body.GetProperty(ProblemDetail).GetString()).IsEqualTo(AdmissionDetail);
        await Assert.That(body.GetProperty(ProblemErrorCode).GetString()).IsEqualTo("OwnershipLost");
    }

    private static async Task RequireStatusAsync(HttpClient http, string path, int expected,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(http.BaseAddress!, path), cancellationToken).ConfigureAwait(false);
        await Assert.That((int)response.StatusCode).IsEqualTo(expected).Because(TwoRf3MembershipProtocol.MissingState);
    }

    private static async Task RequireRejectedAsync(bool succeeded, Problem? problem)
    {
        await Assert.That(succeeded).IsFalse();
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Type).IsEqualTo("urn:keyload:error:OwnershipLost");
        await Assert.That(problem.Title).IsEqualTo("OwnershipLost");
        await Assert.That(problem.StatusCode).IsEqualTo(503);
        await Assert.That(problem.Detail).IsEqualTo(AdmissionDetail);
        await Assert.That(problem.ErrorCode).IsEqualTo("OwnershipLost");
    }
}
