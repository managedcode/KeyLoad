using System.Collections.Immutable;

namespace KeyLoad.Core.Features.DatabaseComposition;

internal static class CompositionOutcomeAuthorityAlias
{
    internal const string Value = "keyload.core.composition-outcome-authority.v1";
}

/// <summary>Private canonical row authority selected by a committed composition.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(CompositionOutcomeAuthorityAlias.Value)]
internal sealed record CompositionOutcomeAuthority(
    [property: Orleans.Id(0)] ImmutableArray<EntityRef> References);
