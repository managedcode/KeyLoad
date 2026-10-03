namespace KeyLoad.Core;

internal static class ChangeFeedCursorContract
{
    internal const string Alias = "keyload.core.v1.ChangeFeedClaims";
    internal const int PurposeField = 0;
    internal const int IncarnationField = 1;
    internal const int PartitionField = 2;
    internal const int CollectionField = 3;
    internal const int PrincipalIdField = 4;
    internal const int PolicyEpochField = 5;
    internal const int SchemaVersionField = 6;
    internal const int VisibilityEpochField = 7;
    internal const int AfterField = 8;
    internal const int ExpiresAtField = 9;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ChangeFeedCursorContract.Alias)]
internal sealed record ChangeFeedClaims(
    [property: global::Orleans.Id(ChangeFeedCursorContract.PurposeField)] string Purpose,
    [property: global::Orleans.Id(ChangeFeedCursorContract.IncarnationField)] Guid Incarnation,
    [property: global::Orleans.Id(ChangeFeedCursorContract.PartitionField)] PartitionRef Partition,
    [property: global::Orleans.Id(ChangeFeedCursorContract.CollectionField)] string Collection,
    [property: global::Orleans.Id(ChangeFeedCursorContract.PrincipalIdField)] string PrincipalId,
    [property: global::Orleans.Id(ChangeFeedCursorContract.PolicyEpochField)] long PolicyEpoch,
    [property: global::Orleans.Id(ChangeFeedCursorContract.SchemaVersionField)] long SchemaVersion,
    [property: global::Orleans.Id(ChangeFeedCursorContract.VisibilityEpochField)] long VisibilityEpoch,
    [property: global::Orleans.Id(ChangeFeedCursorContract.AfterField)] long After,
    [property: global::Orleans.Id(ChangeFeedCursorContract.ExpiresAtField)] DateTimeOffset ExpiresAt);
