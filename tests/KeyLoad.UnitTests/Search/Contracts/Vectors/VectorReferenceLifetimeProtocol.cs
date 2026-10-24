using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.Search;

internal static class VectorReferenceLifetimeProtocol
{
    internal const string Target = "lifetime__derived";
    internal const string StreamSet = "lifetime__events";
    internal const string StreamId = "source-events";
    internal const string FirstEvent = "before-drop";
    internal const string FreshEvent = "after-recreation";
    internal const string EventType = "source-state";
    internal const string EventJson = "{}";
    internal const string TargetJson = "{\"embedding\":[1,0]}";
    internal const string Input = "/value";
    internal const string Field = "/embedding";
    internal const string Reducer = "lifetime-execution-reducer";
    internal const string ReducerVersion = "v1";
    internal const int FirstRevision = 1;
    internal const int FreshEventRevision = 2;
    internal const int OneResult = 1;
    internal const int FirstCold = 0;
    internal const int ColdCuts = 2;
    internal static readonly VectorSpace Space = new("lifetime-execution-space", 2, DistanceMetric.Cosine, "lifetime-execution-model", "v1");

    internal static ApplyVectorProjection Request(EntityRef source, bool fresh)
        => new(new(DropRuntimeProtocol.Partition, StreamSet, StreamId),
            fresh ? FreshEventRevision : FirstRevision, fresh ? FreshEvent : FirstEvent, source, FirstRevision,
            Input, Reducer, ReducerVersion, FirstRevision,
            new(Target, DropRuntimeProtocol.Row, Field, [1, 0], Space, FirstRevision));
}
