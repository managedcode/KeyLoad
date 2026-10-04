using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class RabbitReplicaProof
{
    private const int PollMilliseconds = 250;
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

    public static async Task<(string Version, ClusterEvidence Evidence)> VerifyAsync(HttpClient management,
        string queue, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadinessTimeout);
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
            { await Task.Delay(PollMilliseconds, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ComparisonFailureException("RabbitReplicaReadinessTimeout"); }
        }
    }

}
