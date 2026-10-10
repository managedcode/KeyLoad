namespace KeyLoad.UnitTests.Features.Search;

internal static class SearchMcpCatalogEntries
{
    private const string AnnRead = "keyload_search_ann_read";
    private const string AnnReadRoute = "/v1/search/ann";
    private const string AnnMaintain = "keyload_search_ann_maintain";
    private const string AnnMaintainRoute = "/v1/search/ann/maintain";
    private const string TextMaintain = "keyload_search_text_maintain";
    private const string TextMaintainRoute = "/v1/search/text/maintain";

    internal static System.Collections.Immutable.ImmutableArray<(string Name, string Route, KeyLoad.Orleans.GrainReadKind? ReadKind, OperationKind? CommandKind)> All { get; } =
    [
        (AnnRead, AnnReadRoute, KeyLoad.Orleans.GrainReadKind.ApproximateSearch, null),
        (AnnMaintain, AnnMaintainRoute, null, OperationKind.MaintainAnnIndex),
        (TextMaintain, TextMaintainRoute, null, OperationKind.MaintainTextIndex),
        ("keyload_search_text_online_maintain", "/v1/search/text/online/maintain", null, OperationKind.MaintainOnlineTextIndex),
    ];
}
