using System.Collections.Immutable;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Defines the immutable Q1/Q2 capability vocabulary and exposes the configured result ceiling.</summary>
internal static class QueryCapabilityCatalog
{
    private const int ManifestVersion = 1;
    private const int QueryVersion = SqlOperationProtocol.DefaultQueryDialectVersion;
    private const string RuntimeDialect = "TSQL.ScriptDOM180";
    private const string Scope = "atomicPartition";
    private const string ScalarType = "decimal";
    private const string MissingSemantics = "distinctFromNull";
    private const string SqlSurface = "SQL";
    private const string JsonSurface = "JSON";
    private const string CSharpSurface = "C#";
    private const string ComparisonCapability = "comparison";
    private const string AndCapability = "AND";
    private const string OrCapability = "OR";
    private const string NotCapability = "NOT";
    private const string InCapability = "IN";
    private const string NotInCapability = "NOT IN";
    private const string BetweenCapability = "BETWEEN";
    private const string NotBetweenCapability = "NOT BETWEEN";
    private const string NullCapability = "IS NULL";
    private const string NotNullCapability = "IS NOT NULL";
    private const string DistinctCapability = "IS DISTINCT FROM";
    private const string NotDistinctCapability = "IS NOT DISTINCT FROM";
    private const string AstMissingCapability = "AST.IS MISSING";
    private const string Q1Profile = "Q1";
    private const string FeedProfile = "documentChangeFeed";
    private const string LiveProfile = "scalarLiveQuery";
    private const string ModelProfile = "modelViews";
    private const string GraphSearchProfile = "graph-search";
    private const string AstExplainProfile = "AST.Explain";
    private const string JoinProfile = "Q2.InnerJoin";
    private const string LeftPredicateProfile = "Q2.InnerJoin.LeftPredicate";
    private const string QueueJoinProfile = "Q2.QueueEntityRefJoin";
    private const string EventJoinProfile = "Q2.EventEntityRefJoin";
    private const string Q1OffsetProfile = "Q1.Offset";
    private const string Q2OffsetProfile = "Q2.InnerJoin.Offset";
    private const string AstNullOrderingProfile = "Q1.NullOrdering";
    private const string SyntaxProfile = "TSQL.ScriptDOM180";
    private const string TopProfile = "TSQL.Top";
    private const string OffsetFetchProfile = "TSQL.OffsetFetch";
    private const string DistinctProfile = "TSQL.NullDistinct";
    private const string AllProfile = "TSQL.SelectAll";
    private const string AliasProfile = "TSQL.ImplicitProjectionAlias";
    private const string DefaultJoinProfile = "TSQL.DefaultInnerJoin";
    private const string OrdinalProfile = "TSQL.OrdinalOrder";
    private const string NamedParameterProfile = "TSQL.NamedParameters";
    private const string WireSlotProfile = "TSQL.SessionSlots";
    private const string DecimalProfile = "TSQL.DecimalScalar";
    private const string NationalStringProfile = "TSQL.NationalStringLiteral";
    private static readonly ImmutableArray<int> SupportedVersions = [QueryVersion];
    internal static QueryCapabilityManifest Create() => new(
        ManifestVersion, QueryVersion, RuntimeDialect, Scope, ScalarType, MissingSemantics,
        [SqlSurface, JsonSurface, CSharpSurface],
        [ComparisonCapability, AndCapability, OrCapability, NotCapability, InCapability, NotInCapability, BetweenCapability, NotBetweenCapability, NullCapability, NotNullCapability, DistinctCapability, NotDistinctCapability, AstMissingCapability],
        true, true,
        [Q1Profile, FeedProfile, LiveProfile, ModelProfile, JoinProfile, LeftPredicateProfile, QueueJoinProfile, EventJoinProfile, Q1OffsetProfile, Q2OffsetProfile, AstNullOrderingProfile, SyntaxProfile, TopProfile, OffsetFetchProfile, DistinctProfile, AllProfile, AliasProfile, DefaultJoinProfile, OrdinalProfile, NamedParameterProfile, WireSlotProfile, DecimalProfile, NationalStringProfile, GraphSearchProfile, AstExplainProfile],
        SupportedVersions, SupportedVersions);
}
