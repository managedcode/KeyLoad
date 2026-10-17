using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleBoundSeed(ImmutableArray<ResourceOwnerInventoryResultV1> Before,
    ImmutableArray<ImmutableArray<DocumentResult>> Rows, DocumentResult Anchor, DocumentResult Control);
