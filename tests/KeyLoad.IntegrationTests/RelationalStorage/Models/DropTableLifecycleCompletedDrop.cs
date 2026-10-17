using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleCompletedDrop(DropTableLifecycleTable Table, Guid ParentId,
    DropTableResult Original, DropTableLifecycleFreshState Fresh, ImmutableArray<EntityRef> OldReferences);
