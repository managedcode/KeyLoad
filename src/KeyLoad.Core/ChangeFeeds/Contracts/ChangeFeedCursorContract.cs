namespace KeyLoad.Core;

internal static class ChangeFeedCursorContract
{
    internal const string Alias = "keyload.core.ChangeFeedClaims";
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
