using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnStartedSearch(Task<AnnSearchResult> Search, AnnWorkBudget Budget);
