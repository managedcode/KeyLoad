using KeyLoad.Query.Search;
namespace KeyLoad.UnitTests.Search;

internal readonly record struct PackedAnnDeadlineSearch(
    Task<AnnSearchResult> Search,
    AnnWorkBudget Budget,
    PackedAnnDeadlineObservation Observation);
