using System.Collections.Immutable;

namespace KeyLoad.Core.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphSourceResultV1.Alias)]
internal sealed record RemoteGraphSourceResultV1(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteGraphIntentV1> Intents,
    [property: global::Orleans.Id(2)] RemoteGraphSourceMutationV1 Original)
{
    internal const string Alias = "keyload.core.remote-graph.sourceresult.v1";
}
