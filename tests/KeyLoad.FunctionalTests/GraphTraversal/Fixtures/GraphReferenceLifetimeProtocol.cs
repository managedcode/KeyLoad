namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphReferenceLifetimeProtocol
{
    internal const string AnchorCollection = "lifetime_graph_anchor";
    internal const string AnchorId = "anchor";
    internal const string AnchorJson = "{\"anchor\":true}";
    internal const string OriginalGraph = "lifetime_graph_original";
    internal const string FreshGraph = "lifetime_graph_fresh";
    internal const string EdgeId = "linked-row";
    internal const string DeniedEdge = "stale-link";
    internal const string Label = "references";
    internal const string Attributes = "{}";
    internal const string Projection = "eventual-reverse";
    internal const int Version = 1;
    internal const int Revision = 1;
    internal const int Depth = 1;
    internal const int Vertices = 2;
    internal const int Edges = 1;
    internal const int LocalDelivery = 0;
    internal const int FirstCold = 0;
    internal const int ColdCuts = 2;
    internal const int FusionConstant = 60;
    internal const int FirstRank = 1;
    internal const double RetrieverWeight = 1d;
}
