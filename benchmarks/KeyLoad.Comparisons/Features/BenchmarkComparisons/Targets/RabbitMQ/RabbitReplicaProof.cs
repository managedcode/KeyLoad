using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class RabbitReplicaProof
{
    public static async Task<(string Version, ClusterEvidence Evidence)> VerifyAsync(HttpClient management, string queue,
        ComparisonTopology topology, IOptions<ComparisonLifecycleOptions> lifecycleOptions, CancellationToken cancellationToken)
    {
        const string RabbitReplicaReadinessTimeoutDetail = "RabbitReplicaReadinessTimeout";

        var lifecycle = lifecycleOptions.Value;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(lifecycle.ReadinessTimeout);
        while (true)
        {
            try
            {
                var proof = await RabbitQuorumProbe.TryReadReadyAsync(management, queue, topology, deadline.Token);
                if (proof is { } ready)
                {
                    return ready;
                }
            }
            catch (HttpRequestException) when (!deadline.IsCancellationRequested) { }
            catch (JsonException) when (!deadline.IsCancellationRequested) { }
            try
            { await Task.Delay(lifecycle.HttpReadinessPollInterval, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ComparisonFailureException(RabbitReplicaReadinessTimeoutDetail); }
        }
    }

}
