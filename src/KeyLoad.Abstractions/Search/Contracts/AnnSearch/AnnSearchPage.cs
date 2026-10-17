using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns one completed authorized vector page and its actual read-cut metadata.</summary>
/// <param name="Version">The current page contract version.</param>
/// <param name="Documents">Fully projected authorized documents and vector rank-fusion scores.</param>
/// <param name="Position">The canonical position of the same authorized read cut.</param>
/// <param name="Mode">The actual completed native search mode.</param>
/// <param name="CompleteTopK">True only when the eligible top-k was exhaustively ranked.</param>
/// <param name="RequestedMode">The validated explicit request intent, independent from actual execution mode.</param>
/// <param name="IndexGeneration">The requested and validated provisioned generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnnSearchContractAliases.Page)]
public sealed record AnnSearchPage(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] ImmutableArray<RankedDocument> Documents,
    [property: Orleans.Id(2)] long Position,
    [property: Orleans.Id(3)] AnnPageMode Mode,
    [property: Orleans.Id(4)] bool CompleteTopK,
    [property: Orleans.Id(5)] long IndexGeneration,
    [property: Orleans.Id(6)] AnnPageMode RequestedMode);
