namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record CompositionReverseFinalImageState(EntityRef Bound, CommandRequest Request,
    ReplicatedOperation Original, CommitReceipt Receipt, ReplicatedOperation Seed, CommitReceipt SeedReceipt);
