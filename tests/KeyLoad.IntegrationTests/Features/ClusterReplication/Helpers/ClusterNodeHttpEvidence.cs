using System.Globalization;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class ClusterNodeHttpEvidence(DistributedApplication app)
{
    private static readonly TimeSpan DiagnosticRequestTimeout = TimeSpan.FromSeconds(3);
    private const int MaximumResponseBytes = 8 * 1_024;
    private const string DiscoveryUnavailable = "Signed peer discovery unavailable.";
    private const string OversizedResponse = "Signed discovery response exceeds the diagnostic byte limit.";
    private const string HttpStatusFormat = "{0} HTTP {1}.";
    private const string ReadyPath = "/health/ready";
    private const string ReadyUnavailable = "Readiness HTTP unavailable; branch Unobserved.";
    private static readonly CompositeFormat HttpStatusTemplate = CompositeFormat.Parse(HttpStatusFormat);

    internal async Task<(string Readiness, string Discovery)> ReadAsync(string name,
        ReadOnlyMemory<byte> peerSecret, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(DiagnosticRequestTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var readiness = ReadyUnavailable;
        var discovery = DiscoveryUnavailable;
        try
        {
            discovery = await ReadDiscoveryAsync(name, peerSecret, deadline.Token).ConfigureAwait(false);
            readiness = await ReadReadyAsync(name, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { }
        return (readiness, discovery);
    }

    private async Task<string> ReadReadyAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(app.GetEndpoint(name, ClusterFixtureProtocol.HttpEndpointName), ReadyPath));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            _ = await ReadBoundedTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return string.Format(CultureInfo.InvariantCulture, HttpStatusTemplate, "Readiness",
                (int)response.StatusCode) + " branch Unobserved.";
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
        { return ReadyUnavailable; }
    }

    private async Task<string> ReadDiscoveryAsync(string name, ReadOnlyMemory<byte> peerSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            using var deadlineTimeout = new CancellationTokenSource(DiagnosticRequestTimeout, TimeProvider.System);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
            using var security = new PeerSecurity(peerSecret, TimeProvider.System, IntegrationRoutingOptions.Discovery());
            using var http = new HttpClient(security.CreateHandler())
            { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(app.GetEndpoint(name, ClusterFixtureProtocol.HttpEndpointName), ReplicaProtocol.DiscoveryPath));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            return response.StatusCode == System.Net.HttpStatusCode.OK
                ? await ReadBoundedTextAsync(response.Content, deadline.Token).ConfigureAwait(false)
                : string.Format(CultureInfo.InvariantCulture, HttpStatusTemplate, DiscoveryUnavailable,
                    (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DiscoveryUnavailable;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or KeyLoad.KeyLoadException)
        {
            return DiscoveryUnavailable;
        }
    }

    private static async Task<string> ReadBoundedTextAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            return OversizedResponse;
        }

        var bytes = new byte[MaximumResponseBytes + 1];
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(bytes, 0, count);
            }

            count += read;
        }

        return OversizedResponse;
    }

}
