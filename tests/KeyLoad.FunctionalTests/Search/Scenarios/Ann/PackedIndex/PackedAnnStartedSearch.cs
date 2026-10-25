using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal readonly record struct PackedAnnStartedSearch(Task<AnnSearchResult> Search, AnnWorkBudget Budget);
