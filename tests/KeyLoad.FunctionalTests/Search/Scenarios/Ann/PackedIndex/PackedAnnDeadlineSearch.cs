using KeyLoad.Query.Search;
namespace KeyLoad.FunctionalTests.Search;

internal readonly record struct PackedAnnDeadlineSearch(
    Task<AnnSearchResult> Search,
    AnnWorkBudget Budget,
    PackedAnnDeadlineObservation Observation);
