using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterSelectionCases
{
    private const string SourceAlias = "d";
    private const string PrefixSource = "d.odd";
    private const string NestedDotSource = "d.odd.name";
    private const string ContactLabelSource = "d.contacts.[0].label";
    private const string ContactSecretSource = "d.contacts.[0].secret";
    private const string DescendantSource = "d.profile.secret.missing";
    private const string Equal = "=";
    private const string ScalarAncestor = "{\"exposed\":7}";
    private const string PublicContact = "{\"exposed\":\"one\"}";
    private const string RawClassified = "{\"exposed\":\"SAFE-PROJECTOR-PRIVATE-CANARY\"}";
    private const string ProfilePath = "/profile";
    private const string DescendantPath = "/profile/secret/missing";
    private const string ContactSecretPath = "/contacts/0/secret";
    private const string ContactLabelPath = "/contacts/0/label";
    private const string PrefixPath = "/odd";
    private const string NestedDotPath = "/odd/name";
    internal const string ObjectPath = "/profile";
    internal const string ArrayPath = "/contacts";
    internal const string ObjectSource = "d.profile";
    internal const string ArraySource = "d.contacts";
    internal const string UnsupportedScalar = "This field is not a scalar value.";
    internal const string DuplicateSource = "d.[odd.name] AS exposed, d.publicNull";
    internal const string DuplicateSqlDetail = "The SQL syntax or query arguments are invalid.";
    internal const string DuplicateAliases = "The query projection has duplicate aliases or an invalid star.";

    internal static ImmutableArray<SafeProjectorOperationClusterSelection> Cases
    {
        get;
    } =
    [
        new(SafeProjectorOperationClusterProtocol.Second, ObjectSource, ProfilePath,
            ScalarAncestor, ScalarAncestor, SafeProjectorOperationClusterProtocol.NestedPath),
        new(SafeProjectorOperationClusterProtocol.First, DescendantSource, DescendantPath,
            SafeProjectorOperationClusterProtocol.TypedNullJson, SafeProjectorOperationClusterProtocol.TypedNullJson, SafeProjectorOperationClusterProtocol.NestedPath),
        new(SafeProjectorOperationClusterProtocol.First, ContactSecretSource, ContactSecretPath,
            SafeProjectorOperationClusterProtocol.TypedNullJson, RawClassified, SafeProjectorOperationClusterProtocol.ArrayPath),
        new(SafeProjectorOperationClusterProtocol.First, ContactLabelSource, ContactLabelPath,
            PublicContact, PublicContact, null),
        new(SafeProjectorOperationClusterProtocol.First, PrefixSource, PrefixPath,
            SafeProjectorOperationClusterProtocol.TypedNullJson, SafeProjectorOperationClusterProtocol.TypedNullJson, null),
        new(SafeProjectorOperationClusterProtocol.First, NestedDotSource, NestedDotPath,
            SafeProjectorOperationClusterProtocol.TypedNullJson, SafeProjectorOperationClusterProtocol.TypedNullJson, null)
    ];

    internal static QueryRequest Sql(PartitionRef partition, string collection, string id, string source)
        => new(partition, SelectPrefix + source + OutputAlias + collection + WhereIdPrefix + id + WhereIdSuffix, AllowFullScan: true);

    internal static AstQueryRequest Ast(PartitionRef partition, string collection, string id, string path)
        => new(partition, new(collection, SourceAlias, [new(path, SafeProjectorOperationClusterProtocol.Exposed)],
            new Comparison(new FieldOperand(SafeProjectorOperationClusterProtocol.IdPath), Equal, ValueOperand.Create(id)),
            [new(SafeProjectorOperationClusterProtocol.IdPath, false)], SafeProjectorOperationClusterProtocol.BothRows), AllowFullScan: true);

    private const string SelectPrefix = "SELECT ";
    private const string OutputAlias = " AS exposed FROM [";
    private const string WhereIdPrefix = "] d WHERE d.id = '";
    private const string WhereIdSuffix = "' ORDER BY d.id";
}
