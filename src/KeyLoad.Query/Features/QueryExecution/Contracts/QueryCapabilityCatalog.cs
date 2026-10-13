using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Defines the immutable Q1/Q2 capability vocabulary and exposes the current operation limits.</summary>
internal static class QueryCapabilityCatalog
{
    private const int ManifestVersion = 1;
    private const int Q1DialectVersion = 1;
    private const int Q2DialectVersion = 2;
    private const string QueryDialectVersion = "Q1";
    private const string AtomicPartitionScope = "atomicPartition";
    private const string DecimalScalarType = "decimal";
    private const string MissingValueSemantics = "distinctFromNull";
    private const string SqlCallerSurface = "SQL";
    private const string JsonCallerSurface = "JSON";
    private const string CSharpCallerSurface = "C#";
    private const string ComparisonCapability = "comparison";
    private const string AndCapability = "AND";
    private const string OrCapability = "OR";
    private const string NotCapability = "NOT";
    private const string InCapability = "IN";
    private const string BetweenCapability = "BETWEEN";
    private const string NotBetweenCapability = "NOT BETWEEN";
    private const string NullCapability = "IS NULL";
    private const string DistinctCapability = "IS DISTINCT FROM";
    private const string NotDistinctCapability = "IS NOT DISTINCT FROM";
    private const string NullDistinctProfile = "SQL.NullDistinct.v1";
    private const string SymmetricBetweenCapability = "BETWEEN SYMMETRIC";
    private const string NotSymmetricBetweenCapability = "NOT BETWEEN SYMMETRIC";
    private const string SymmetricBetweenProfile = "SQL.BetweenSymmetric.v1";
    private const string AsymmetricBetweenCapability = "BETWEEN ASYMMETRIC";
    private const string NotAsymmetricBetweenCapability = "NOT BETWEEN ASYMMETRIC";
    private const string AsymmetricBetweenProfile = "SQL.BetweenAsymmetric.v1";
    private const string SelectAllProfile = "SQL.SelectAll.v1";
    private const string ImplicitProjectionAliasProfile = "SQL.ImplicitProjectionAlias.v1";
    private const string DefaultInnerJoinProfile = "SQL.DefaultInnerJoin.v1";
    private const string OrderUsingProfile = "SQL.OrderUsing.v1";
    private const string NullSynonymProfile = "SQL.NullPredicateSynonyms.v1";
    private const string PositiveNumericProfile = "SQL.PositiveNumericLiteral.v1";
    private const string LeadingDecimalProfile = "SQL.LeadingDecimalLiteral.v1";
    private const string HexIntegerProfile = "SQL.HexadecimalIntegerLiteral.v1";
    private const string EscapeStringProfile = "SQL.EscapeStringLiteral.v1";
    private const string StringContinuationProfile = "SQL.StringContinuation.v1";
    private const string DollarQuotedStringProfile = "SQL.DollarQuotedString.v1";
    private const string BinaryOctalIntegerProfile = "SQL.BinaryOctalIntegerLiteral.v1";
    private const string UnicodeStringProfile = "SQL.UnicodeStringLiteral.v1";
    private const string UnicodeIdentifierProfile = "SQL.UnicodeIdentifier.v1";
    private const string TableShorthandProfile = "SQL.TableShorthand.v1";
    private const string OrdinalOrderProfile = "SQL.OrdinalOrder.v1";
    private const string NumericDigitSeparatorProfile = "SQL.NumericDigitSeparators.v1";
    private const string BooleanPredicateProfile = "SQL.BooleanPredicate.v1";
    private const string BooleanTruthProfile = "SQL.BooleanTruth.v1";
    private const string IsTrue = "IS TRUE";
    private const string IsFalse = "IS FALSE";
    private const string IsUnknown = "IS UNKNOWN";
    private const string IsNotTrue = "IS NOT TRUE";
    private const string IsNotFalse = "IS NOT FALSE";
    private const string IsNotUnknown = "IS NOT UNKNOWN";
    private const string MissingCapability = "IS MISSING";
    private const string DocumentChangeFeedCapability = "documentChangeFeed";
    private const string ScalarLiveQueryCapability = "scalarLiveQuery";
    private const string ModelViewsCapability = "modelViewsV1";
    private const string InnerJoinCapability = "Q2.InnerJoin.v1";
    private const string LeftPredicateJoinCapability = "Q2.InnerJoin.LeftPredicate.v1";
    private const string QueueEntityRefJoinCapability = "Q2.QueueEntityRefJoin.v1";
    private const string EventEntityRefJoinCapability = "Q2.EventEntityRefJoin.v1";
    private const string Q1OffsetCapability = "Q1.Offset.v1";
    private const string Q2OffsetCapability = "Q2.InnerJoin.Offset.v1";
    private const string NullOrderingCapability = "Q1.NullOrdering.v1";
    private const string FetchCountCapability = "SQL.FetchCount.v1";
    private static readonly ImmutableArray<int> SupportedVersions = [Q1DialectVersion, Q2DialectVersion];
    private static readonly ImmutableArray<int> SupportedDialects = [Q1DialectVersion, Q2DialectVersion];

    internal static QueryCapabilityManifest Create(DatabaseLimits limits) => new(ManifestVersion, ManifestVersion, QueryDialectVersion, AtomicPartitionScope, DecimalScalarType, MissingValueSemantics,
        [SqlCallerSurface, JsonCallerSurface, CSharpCallerSurface], [ComparisonCapability, AndCapability, OrCapability, NotCapability, InCapability, BetweenCapability, NotBetweenCapability, NullCapability, MissingCapability, DistinctCapability, NotDistinctCapability, IsTrue, IsFalse, IsUnknown, IsNotTrue, IsNotFalse, IsNotUnknown, SymmetricBetweenCapability, NotSymmetricBetweenCapability, AsymmetricBetweenCapability, NotAsymmetricBetweenCapability, SqlSyntax.IsNull, SqlSyntax.NotNull],
        limits.MaxResults, limits.MaxScanRecords, limits.MaxQueryBytes, limits.MaxQueryDepth, true, true,
        limits.MaxQueryReadBytes, [QueryDialectVersion, DocumentChangeFeedCapability, ScalarLiveQueryCapability, ModelViewsCapability, SqlGraphSearchSyntax.ProfileName, SqlGraphPathSyntax.VersionProfile, InnerJoinCapability, LeftPredicateJoinCapability, QueueEntityRefJoinCapability, EventEntityRefJoinCapability, Q1OffsetCapability, Q2OffsetCapability, NullOrderingCapability, FetchCountCapability, NullDistinctProfile, BooleanTruthProfile, SymmetricBetweenProfile, SelectAllProfile, ImplicitProjectionAliasProfile, DefaultInnerJoinProfile, AsymmetricBetweenProfile, OrderUsingProfile, NullSynonymProfile, PositiveNumericProfile, LeadingDecimalProfile, HexIntegerProfile, EscapeStringProfile, StringContinuationProfile, DollarQuotedStringProfile, BinaryOctalIntegerProfile, UnicodeStringProfile, BooleanPredicateProfile, NumericDigitSeparatorProfile, UnicodeIdentifierProfile, TableShorthandProfile, OrdinalOrderProfile], SupportedVersions, SupportedDialects);
}
