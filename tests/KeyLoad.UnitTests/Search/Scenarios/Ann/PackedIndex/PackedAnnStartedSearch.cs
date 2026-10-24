using KeyLoad.Query.Search;

namespace KeyLoad.UnitTests.Search;

internal readonly record struct PackedAnnStartedSearch(Task<AnnSearchResult> Search, AnnWorkBudget Budget);
