using System.Collections.Immutable;

namespace KeyLoad.Core.Features.DatabaseComposition;

/// <summary>Private canonical row authority selected by a committed composition.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(CompositionOutcomeAuthorityAlias.Value)]
internal sealed record CompositionOutcomeAuthority(
    [property: Orleans.Id(0)] ImmutableArray<EntityRef> References);
