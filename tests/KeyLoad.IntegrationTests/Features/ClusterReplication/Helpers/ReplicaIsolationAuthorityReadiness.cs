using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationFlowProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Observes actual newer-term surviving authority under the original deadline before any next mutation.</summary>
internal static class ReplicaIsolationAuthorityReadiness
{
    internal static async Task<(string Leader, NodeStatus Status)> ObserveAsync(ClusterFixture fixture, string isolated,
        long originalTerm, ReplicaIsolationOwner owner, CancellationToken cancellationToken)
    {
        var survivors = owner.Resources.Where(node => node != isolated).ToArray();
        var observations = new List<ReplicaIsolationAuthorityObservation>();
        var failures = new List<Exception>();
        (string Leader, NodeStatus Status)? authority = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        { authority = await ProbeAsync(fixture, survivors, originalTerm, observations, cancellationToken); }, failures);
        await ServerFailureObserver.ObserveAsync(() => owner.RecordAuthorityAsync(observations, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return authority ?? throw new InvalidOperationException("The original authority observation returned no outcome.");
    }

    private static async Task<(string Leader, NodeStatus Status)> ProbeAsync(ClusterFixture fixture, string[] survivors,
        long originalTerm, List<ReplicaIsolationAuthorityObservation> observations, CancellationToken cancellationToken)
    {
        var attempt = Zero;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = survivors[attempt % survivors.Length];
            attempt = (attempt + First) % survivors.Length;
            using var clientHttp = fixture.App.CreateHttpClient(resource, ClusterFixtureProtocol.HttpEndpointName);
            clientHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(clientHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
            var actual = await client.StatusAsync(cancellationToken);
            if (observations.Count == MaximumRetainedStatusObservations)
            { observations.RemoveAt(Zero); }
            observations.Add(new(resource, actual.Value, actual.Problem?.ErrorCode,
                actual.Problem?.StatusCode, actual.Problem?.Detail));
            if (!actual.IsSuccess)
            {
                if (actual.Problem?.ErrorCode != nameof(ErrorCode.OwnershipLost)
                    || actual.Problem?.StatusCode != ServiceUnavailable || actual.Problem?.Detail != NoLeader)
                { _ = await McpCallerAssertions.SdkSuccessAsync(actual); }
                continue;
            }
            var status = actual.Value ?? throw new InvalidOperationException("The native status observation is empty.");
            var leader = status.Leader is null ? null : new Uri(status.Leader).Host;
            if (leader is not null && survivors.Contains(leader, StringComparer.Ordinal) && status.ConsensusTerm > originalTerm)
            { return (leader, status); }
        }
    }
}
