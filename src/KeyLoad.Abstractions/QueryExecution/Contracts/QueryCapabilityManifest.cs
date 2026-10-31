using System.Collections.Immutable;

namespace KeyLoad.Query;

/// <summary>Describes supported query capabilities and the configured result ceiling.</summary>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="AstVersion">The query AST version.</param>
/// <param name="SqlDialect">The supported SQL dialect name.</param>
/// <param name="Scope">The scope in which the manifest applies.</param>
/// <param name="NumericPolicy">The numeric comparison policy.</param>
/// <param name="MissingPolicy">The policy for missing values.</param>
/// <param name="Adapters">The supported query adapters.</param>
/// <param name="Predicates">The supported predicate forms.</param>
/// <param name="FullScanRequiresOptIn">Whether full scans require explicit caller opt-in.</param>
/// <param name="ReadOnly">Whether queries described by this manifest are read-only.</param>
/// <param name="ReadProfiles">The available read profiles.</param>
/// <param name="SupportedAstVersions">The exact supported AST versions.</param>
/// <param name="SupportedQueryDialectVersions">The exact supported query-language versions.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.QueryCapabilityManifest)]
public sealed record QueryCapabilityManifest([property: Orleans.Id(0)] int ProtocolVersion, [property: Orleans.Id(1)] int AstVersion, [property: Orleans.Id(2)] string SqlDialect, [property: Orleans.Id(3)] string Scope,
    [property: Orleans.Id(4)] string NumericPolicy, [property: Orleans.Id(5)] string MissingPolicy, [property: Orleans.Id(6)] ImmutableArray<string> Adapters, [property: Orleans.Id(7)] ImmutableArray<string> Predicates,
    [property: Orleans.Id(12)] bool FullScanRequiresOptIn, [property: Orleans.Id(13)] bool ReadOnly, [property: Orleans.Id(15)] ImmutableArray<string> ReadProfiles,
    [property: Orleans.Id(16)] ImmutableArray<int> SupportedAstVersions,
    [property: Orleans.Id(17)] ImmutableArray<int> SupportedQueryDialectVersions);
