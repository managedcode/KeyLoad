using KeyLoad.Query.Features.Search;
namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnDeadlineSearch(
    Task<AnnSearchResult> Search,
    AnnWorkBudget Budget,
    PackedAnnDeadlineObservation Observation);
