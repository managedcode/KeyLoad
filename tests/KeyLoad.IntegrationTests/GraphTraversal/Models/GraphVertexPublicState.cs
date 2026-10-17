namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class GraphVertexPublicState(GraphIncomingRf3Seed seed)
{
    internal const string EdgeId = "canonical-vertex-edge";
    internal const string NewId = "canonical-vertex-new";
    internal const string WitnessId = "canonical-vertex-refused";
    internal const string MissingId = "canonical-vertex-missing";
    internal const string HealthyId = "canonical-vertex-healthy";
    internal const string Label = "canonical-vertex";
    internal const string Json = "{}";

    internal GraphIncomingRf3Seed Seed { get; } = seed;
    internal EntityRef NewVertex { get; } = new(seed.TargetPartition, GraphIncomingRf3Scenario.Collection, NewId);
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Original { get; } = [];
    internal List<(CommandRequest Command, ErrorCode Code)> Refusals { get; } = [];
    internal EdgeRecord[] Edges { get; set; } = [];
}
