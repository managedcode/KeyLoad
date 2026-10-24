using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal sealed class DocumentLiveProfileClusterState(PartitionRef partition, string principalId, string key)
{
    internal const string Collection = "live-profile-documents";
    internal const string Secret = "PRIVATE-LIVE-PROFILE";
    internal const string Unsupported = "The live-query profile requires an unordered complete scalar result set.";
    internal const string CursorInvalidated = "The change-feed cursor requires a fresh authorized snapshot.";
    internal PartitionRef Partition { get; } = partition;
    internal string PrincipalId { get; } = principalId;
    internal string Key { get; } = key;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Commands { get; } = [];
    internal string OriginalCursor { get; set; } = string.Empty;
    internal string FreshCursor { get; set; } = string.Empty;
    internal AstQueryRequest Query => new(Partition, new(Collection, null, [new("*", "*")],
        new Comparison(new FieldOperand("/value"), ">", ValueOperand.Create(0)), [], 10), AllowFullScan: true);
    internal AstQueryRequest Model(ModelQuerySourceKind kind) => Query with
    {
        Query = Query.Query with
        {
            ModelSource = new(kind, kind == ModelQuerySourceKind.Events ? "stream" : Collection, 1L)
        }
    };
    internal EntityRef Reference(string id) => new(Partition, Collection, id);
    internal static string Json(int value) => "{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"secret\":\"" + Secret + "\"}";
    internal static QueryRow Row(string id, long revision, int value) => new(id, revision,
        "{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", true, ["/secret"]);
    internal static QueryRow[] Rows(int phase) => phase switch
    {
        0 => [Row("a", 1L, 1)],
        1 => [Row("b", 1L, 3)],
        _ => [Row("b", 1L, 3), Row("c", 1L, 5)]
    };
    internal PrincipalRecord Principal(long epoch, bool feed, bool revoked) => new(PrincipalId, Partition.TenantId,
        [new(Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead | (feed ? Capability.ChangesRead : Capability.None))], [])
    {
        PolicyEpoch = epoch,
        Revoked = revoked,
        RestrictRows = true,
        OwnerId = "live-owner"
    };
}
