using System.Collections.Immutable;
using KeyLoad;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingMcpTestData
{
    internal const string Tenant = "incoming-tenant";
    internal const string Database = "incoming-database";
    internal const string Domain = "incoming-domain";
    internal const string PartitionKey = "incoming-targets";
    internal const string Collection = "incoming-documents";
    internal const string Graph = "incoming-graph";
    internal const string SourceId = "incoming-source";
    internal const string TargetId = "incoming-target";
    internal const string EdgeId = "incoming-edge";
    internal const string CrossEdgeId = "incoming-cross-edge";
    internal const string CrossSourceId = "incoming-cross-source";
    internal const string SourcePartitionKey = "incoming-source-partition";
    internal const string Label = "references";
    internal const string AttributesJson = "{\"weight\":2}";
    internal const string Projection = "eventual-reverse.v1";
    internal const long SourceRevision = 4;
    internal const long CrossSourceRevision = 7;
    internal const long LocalDeliveredRevision = 0;
    internal const long DeliveredRevision = 4;
    internal const long CutPosition = 19;
    internal const int Limit = 8;
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static readonly PartitionRef SourcePartition = new(Tenant, Database, Domain, SourcePartitionKey);
    internal static readonly EntityRef Source = new(Partition, Collection, SourceId);
    internal static readonly EntityRef CrossSource = new(SourcePartition, Collection, CrossSourceId);
    internal static readonly EntityRef Target = new(Partition, Collection, TargetId);

    internal static ReadIncomingGraphEdgesRequestV1 Request() => new(1, Target, Graph, Limit);

    internal static GraphIncomingEdgesPageV1 Page()
    {
        var local = new GraphIncomingEdgeRowV1(
            new EdgeRecord(EdgeId, Source, Target, Label, AttributesJson, SourceRevision), LocalDeliveredRevision);
        var cross = new GraphIncomingEdgeRowV1(
            new EdgeRecord(CrossEdgeId, CrossSource, Target, Label, AttributesJson, CrossSourceRevision), DeliveredRevision);
        return new GraphIncomingEdgesPageV1(1, [local, cross], CutPosition, Projection);
    }
}
