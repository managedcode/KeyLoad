using System.Runtime.InteropServices;
using KurrentDB.Client;
using Microsoft.Extensions.Options;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.Comparisons.Targets;

internal sealed record KurrentClusterProof(KurrentDBClient[] NodeClients);

internal static class KurrentClusterVerifier
{
    public static async Task<KurrentClusterProof> VerifyAsync(string connectionString, HttpClient[] httpClients, ComparisonTopology topology,
        TimeSpan timeout, IOptions<ComparisonLifecycleOptions> options, CancellationToken cancellationToken)
    {
        ValidateHttpClientSet(httpClients, topology);
        await ReadReadyViewsAsync(clients: httpClients, topology: topology, timeout: timeout, cancellationToken: cancellationToken,
            options: options);
        var nodeClients = new List<KurrentDBClient>(httpClients.Length);
        try
        {
            foreach (var client in httpClients)
            {
                nodeClients.Add(new KurrentDBClient(KurrentNativeSettings.CreateDirectNode(connectionString, client.BaseAddress!)));
            }

            return new KurrentClusterProof(nodeClients.ToArray());
        }
        catch (Exception)
        {
            foreach (var client in nodeClients)
            {
                await client.DisposeAsync();
            }

            throw;
        }
    }

    public static Task<ClusterEvidence> VerifyCopyAsync(KurrentDBClient writer, KurrentDBClient[] nodeClients, HttpClient[] httpClients,
        ComparisonTopology topology, string stream, KurrentEventData eventData, KurrentStreamOwnership ownership, TimeSpan timeout,
        IOptions<ComparisonLifecycleOptions> options, CancellationToken cancellationToken)
        => KurrentReplicaProbe.VerifyCopyAsync(writer: writer, nodeClients: nodeClients, httpClients: httpClients, topology: topology,
            stream: stream, eventData: eventData, ownership: ownership, timeout: timeout, cancellationToken: cancellationToken,
            options: options);

    internal static async Task<KurrentGossipView[]> ReadReadyViewsAsync(HttpClient[] clients, ComparisonTopology topology, TimeSpan timeout,
        IOptions<ComparisonLifecycleOptions> options, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var views = await TryReadReadyViewsAsync(clients, topology, limit);
                if (views is not null)
                {
                    return views;
                }
                await Task.Delay(options.Value.KurrentReadinessPollInterval, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(KurrentConstants.ClusterMembership);
        }
    }

    internal static ClusterEvidence CreateEvidence(KurrentGossipView[] views, ComparisonTopology topology,
        KurrentAppendCut? appendCut = null)
    {
        const int SingleItemCount = 1;

        var nodes = views.Select(view => view.LocalMember).OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();
        var observations = nodes.SelectMany(member => new[]
        {
            KurrentConstants.ObservationNodeCount + member.Id,
            KurrentConstants.ObservationRoles + member.State,
            KurrentConstants.ObservationVersion + member.Version,
            KurrentConstants.ObservationEndpoint + member.HttpEndpointIp + KurrentConstants.StreamSeparator + member.HttpEndpointPort,
            KurrentConstants.ObservationLastCommit + member.Commit,
            KurrentConstants.ObservationWriterCheckpoint + member.Writer,
            KurrentConstants.ObservationChaserCheckpoint + member.Chaser
        }).ToList();
        if (appendCut is { } cut)
        {
            observations.Add(KurrentConstants.ObservationAppendCommit + cut.CommitPosition);
            observations.Add(KurrentConstants.ObservationAppendPrepare + cut.PreparePosition);
            observations.Add(KurrentConstants.ObservationCheckpointEvidence);
        }
        return new ClusterEvidence(nodes.Length, nodes.Length,
            ComparisonTopologies.NodeCount(topology) > SingleItemCount ? KurrentConstants.HealthyState : KurrentConstants.SingleState,
            ImmutableCollectionsMarshal.AsImmutableArray(observations.ToArray()));
    }

    private static async Task<KurrentGossipView[]> ReadViewsAsync(HttpClient[] clients, CancellationToken cancellationToken)
    {
        var views = new List<KurrentGossipView>(clients.Length);
        foreach (var client in clients)
        {
            views.Add(await KurrentGossipView.ReadAsync(client, cancellationToken));
        }

        return views.ToArray();
    }

    private static async Task<KurrentGossipView[]?> TryReadReadyViewsAsync(HttpClient[] clients,
        ComparisonTopology topology, CancellationTokenSource limit)
    {
        try
        {
            var views = await ReadViewsAsync(clients, limit.Token);
            return KurrentClusterMembers.IsReady(views, topology) ? views : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!limit.IsCancellationRequested)
        {
            return null;
        }
    }

    private static void ValidateHttpClientSet(HttpClient[] clients, ComparisonTopology topology)
    {
        var expectedCount = ComparisonTopologies.NodeCount(topology);
        if (clients.Length != expectedCount || clients.Any(client => client.BaseAddress is null) ||
            clients.Select(client => client.BaseAddress!.Authority).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expectedCount)
        {
            throw new ComparisonFailureException(KurrentConstants.InvalidTopology);
        }
    }

}
