namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionReverseFinalImageData
{
    internal const string Root = "root";
    internal const string Collection = "composition-final-image-docs";
    internal const string Graph = "composition-final-image-graph";
    internal const string Queue = "composition-final-image-queue";
    internal const string Id = "start";
    internal const string Prefix = "final-image-";
    internal const string Json = "{\"value\":7}";
    internal const string Unavailable = "The graph vertex is unavailable.";
}

internal sealed record CompositionReverseFinalImageState(EntityRef Bound, CommandRequest Request,
    ReplicatedOperation Original, CommitReceipt Receipt, ReplicatedOperation Seed, CommitReceipt SeedReceipt);
