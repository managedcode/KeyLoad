using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkPendingRf3State(SampleChunkJobRevocationScenario Scope,
    ImmutableArray<SampleChunkPendingRf3Item> Admitted, SampleChunkPendingRf3Item Candidate)
{
    internal SampleChunkPendingRf3Item? Fresh { get; set; }
}
