using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationRf3Observation
{
    private const string Completed = "Physical owner registration completed.";
    private const string Failed = "Physical owner registration stopped with ";
    private static readonly Uri Health = new("/health/physical-owner-registration", UriKind.Relative);

    internal static Task WaitAsync(DistributedApplication app, CancellationToken token)
        => WaitAsync(app, null, token);

    internal static async Task WaitAsync(DistributedApplication app, PhysicalOwnerRegistrationRf3Evidence? evidence,
        CancellationToken token)
    {
        evidence?.Enter(PhysicalOwnerRegistrationRf3Phase.LogService);
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            evidence?.BeginNode(node);
            await WaitForCompletionAsync(logs, node, evidence, token).ConfigureAwait(false);
            using var http = app.CreateHttpClient(node);
            evidence?.Enter(PhysicalOwnerRegistrationRf3Phase.HealthSend);
            using var response = await http.GetAsync(Health, token).ConfigureAwait(false);
            evidence?.HealthReceived(response.StatusCode);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    private static async Task WaitForCompletionAsync(ResourceLoggerService logs, string node, PhysicalOwnerRegistrationRf3Evidence? evidence,
        CancellationToken token)
    {
        evidence?.Enter(PhysicalOwnerRegistrationRf3Phase.LogWatch);
        await foreach (var batch in logs.WatchAsync(node).WithCancellation(token).ConfigureAwait(false))
        {
            foreach (var line in batch)
            {
                if (line.Content.Contains(Failed, StringComparison.Ordinal))
                { evidence?.MarkFailed(); throw new InvalidOperationException("The native owner registration failed."); }
                if (line.Content.Contains(Completed, StringComparison.Ordinal))
                { evidence?.MarkCompleted(); return; }
            }
        }
        throw new InvalidOperationException("The native owner registration stream ended before completion.");
    }
}
