namespace KeyLoad.Core;

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
