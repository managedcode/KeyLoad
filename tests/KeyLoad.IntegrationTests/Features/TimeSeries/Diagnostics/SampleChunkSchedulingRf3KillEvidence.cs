using System.Text;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3KillEvidence
{
    internal static async Task WriteAsync(IReadOnlyDictionary<string, ContainerRuntimeKillReceipt> originals)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var original = JsonDefaults.Serialize(originals[node]);
            if (original.Length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.RecordLimitExceeded); }
            // Original inspect/kill bytes contain resource/runtime identities only, never caller credentials or data.
            await TestContext.Current!.OutputWriter.WriteLineAsync(Encoding.UTF8.GetString(original));
        }
    }
}
