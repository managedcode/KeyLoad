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

    internal static async Task WaitAsync(DistributedApplication app, CancellationToken token)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await WaitForCompletionAsync(logs, node, token).ConfigureAwait(false);
            using var http = app.CreateHttpClient(node);
            using var response = await http.GetAsync(Health, token).ConfigureAwait(false);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    private static async Task WaitForCompletionAsync(ResourceLoggerService logs, string node, CancellationToken token)
    {
        await foreach (var batch in logs.WatchAsync(node).WithCancellation(token).ConfigureAwait(false))
        {
            foreach (var line in batch)
            {
                if (line.Content.Contains(Failed, StringComparison.Ordinal))
                { throw new InvalidOperationException("The native owner registration failed."); }
                if (line.Content.Contains(Completed, StringComparison.Ordinal))
                { return; }
            }
        }
        throw new InvalidOperationException("The native owner registration stream ended before completion.");
    }
}
