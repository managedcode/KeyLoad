namespace KeyLoad.ClusterTests.Authorization;

internal static class SchemaVisibleProjectionClusterProtocol
{
    internal const string SchemaGrant = "safe.operation.schema";
    internal const string BindingDetail = "The requested field binding is unavailable.";
    internal const string IdentifierDetail = "An identifier is missing, too long or contains control characters.";
    internal const string EmptyGrant = "";
    internal const string Alias = "d";
    internal const string RightAlias = "r";
    internal const string ValueAlias = "exposed";
    internal const string UnknownPath = "/doesNotExist";
    internal const string Equal = "=";
    internal const string Star = "*";
    internal const string FullScan = "bounded-full-scan";
    internal const string Point = "point";
    internal const string SourcePrefix = "SELECT ";
    internal const string FromPrefix = " FROM [";
    internal const string FromSuffix = "] d";
    internal const string AliasSuffix = " AS exposed";
    internal const string WherePrefix = " WHERE ";
    internal const string WhereSuffix = " = 'open'";
    internal const string OrderPrefix = " ORDER BY ";
    internal const string PublicSource = "d.status";
    internal const string ProtectedSource = "d.[odd.name]";
    internal const string UnknownSource = "d.doesNotExist";
    internal const string JoinPrefix = "SELECT * FROM [";
    internal const string JoinMiddle = "] d INNER JOIN [";
    internal const string JoinSuffix = "] r ON d.status = r.status";
    internal const long MaskedEpoch = 2;
    internal const long RawEpoch = 3;
    internal const long SchemaEpoch = 4;
    internal const long RevokedEpoch = 5;
    internal const long FinalEpoch = 6;
    internal const long RootEpoch = 7;
    internal const long HiddenVersion = 2;
    internal const long MixedVersion = 3;
    internal const int First = 0;
    internal const int Second = 1;
    internal const int Third = 2;
    internal const int Limit = 10;
    internal const int QueryDialect = SqlOperationProtocol.DefaultQueryDialectVersion;
    internal const int ColdCuts = 2;
}
