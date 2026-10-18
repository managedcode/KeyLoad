using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal static class VectorReferenceLifetimeNativeProtocol
{
    internal const string Target = "lifetime_native_derived";
    internal const string StreamSet = "lifetime_native_events";
    internal const string StreamId = "source-events";
    internal const string FirstEvent = "before-drop";
    internal const string FreshEvent = "after-recreation";
    internal const string EventType = "source-state";
    internal const string EventJson = "{}";
    internal const string TargetJson = "{\"embedding\":[1,0]}";
    internal const string Input = "/value";
    internal const string Field = "/embedding";
    internal const string Reducer = "lifetime-native-reducer";
    internal const string ReducerVersion = "v1";
    internal const int FirstRevision = 1;
    internal const int FreshEventRevision = 2;
    internal const int OneResult = 1;
    internal const int FirstCold = 0;
    internal const int ColdCuts = 2;
    internal static readonly VectorSpace Space = new("lifetime-native-space", 2, DistanceMetric.Cosine, "lifetime-native-model", "v1");

    internal static ApplyVectorProjection Request(EntityRef source, bool fresh)
        => new(new(NativeDropRuntimeProtocol.Partition, StreamSet, StreamId),
            fresh ? FreshEventRevision : FirstRevision, fresh ? FreshEvent : FirstEvent, source, FirstRevision,
            Input, Reducer, ReducerVersion, FirstRevision,
            new(Target, NativeDropRuntimeProtocol.Row, Field, [1, 0], Space, FirstRevision));
}
