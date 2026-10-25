using System.Text.Json;

namespace KeyLoad.UnitTests.CodeQuality.Assertions;

internal static class ProductionSourceManifestContributorAssertions
{
    private const int ExpectedUnitRows = 42;
    private const int ExpectedClusterRows = 9;
    private const int MaximumIdentityFieldCharacters = 512;
    private const int MaximumOperationCharacters = 4096;
    private const string RegistryPath = "scripts/Features/CodeQuality/functional-coverage.product-contributors.json";
    private const string QueryContractPath = "scripts/Features/CodeQuality/functional-coverage.contract.json";
    private const string SchemaVersionProperty = "schemaVersion";
    private const string ContributorsProperty = "contributors";
    private const string ExactCasesProperty = "exactCases";
    private const string UnitSuite = "unit";
    private const string RecoverySuite = "recovery";
    private const string ClusterSuite = "cluster";
    private const string MismatchedLeafMethod = "MismatchedLeafRequestPartitionFailsBeforeAnyStorageRead";
    private const string PartitionSetMethod = "EmptyOversizedAndNullPartitionSetsFailAsValidation";
    private const string OwnershipMethod = "ExpectedOwnerMismatchFailsClosedAndProtectedFieldUseStillApplies";
    private const string InvalidRequestMethod = "DefaultEmptyOversizedDuplicateAndUnsupportedRequestsFailBeforeStorageRead";
    private const string SuiteProperty = "suite";
    private const string ClassNameProperty = "className";
    private const string MethodNameProperty = "methodName";
    private const string InstanceNameProperty = "instanceName";
    private const string RequirementsProperty = "requirements";
    private const string AcceptanceProperty = "acceptance";
    private const string ExecutedModulesProperty = "executedModules";
    private const string OperationProperty = "operationOutcomeAndState";

    private static readonly string[] FieldNames =
    [
        AcceptanceProperty, ClassNameProperty, ExecutedModulesProperty, InstanceNameProperty, MethodNameProperty,
        OperationProperty, RequirementsProperty, SuiteProperty
    ];
    private static readonly string[] Suites = [UnitSuite, RecoverySuite, ClusterSuite];
    private static readonly Dictionary<string, string> StrengthenedQueryOperations = new(StringComparer.Ordinal)
    {
        [MismatchedLeafMethod] = "The invalid leaf/request partition is rejected as Validation before execution reads; seeded valid data is then read through QueryEngine on the same ZoneTree database, with exact full EntityRef, projected JSON/revision and unchanged committed position.",
        [PartitionSetMethod] = "Empty, over-eight and null leaf sets remain Validation with unchanged position; an actual valid QueryEngine request on the seeded same database returns the expected full reference, projected JSON/revision, with position preserved.",
        [OwnershipMethod] = "A wrong server-owned physical tuple returns OwnershipLost with unchanged position, then the correct owner reads the exact row. A protected sort-field denial returns PermissionDenied with unchanged position; a valid same-reader query returns the visible projection and exact persisted-redaction metadata.",
        [InvalidRequestMethod] = "Default/empty/duplicate/over-eight/unsupported/version/AST inputs retain their typed errors and unchanged committed position on a seeded fixture; a valid same-fixture query then returns the complete expected page, full EntityRef, projected JSON and revision."
    };
    private static readonly string[] CandidateIdentities =
    [
        "cluster|KeyLoad.IntegrationTests.QueryExecution.PartitionQueryPublicClusterTests|AcPquery006SdkAndOfficialMcpReturnIndependentFullReferenceOrder|AcPquery006SdkAndOfficialMcpReturnIndependentFullReferenceOrder",
        "cluster|KeyLoad.IntegrationTests.DocumentStorage.McpDocumentCrudParityTests|AcDstore001SdkPatchAndMcpDeleteMatchTheMirroredCrudLifecycle|AcDstore001SdkPatchAndMcpDeleteMatchTheMirroredCrudLifecycle",
        "cluster|KeyLoad.IntegrationTests.DocumentStorage.McpDocumentCrudParityTests|AcDstore001McpPatchAndSdkDeleteMatchTheMirroredCrudLifecycle|AcDstore001McpPatchAndSdkDeleteMatchTheMirroredCrudLifecycle",
        "cluster|KeyLoad.IntegrationTests.DocumentStorage.McpDocumentCrudParityTests|AcDstore001StaleExplicitReplacementIsRejectedWithoutChangingRevisionTwo|AcDstore001StaleExplicitReplacementIsRejectedWithoutChangingRevisionTwo",
        "recovery|KeyLoad.RecoveryTests.DocumentStorage.CommandIdempotencyProcessRecoveryTests|AcDocument006OneHundredCommandRetriesSurviveRealProcessRestart|AcDocument006OneHundredCommandRetriesSurviveRealProcessRestart"
    ];

    internal static async Task AssertAsync(JsonElement actualRows, string repositoryRoot)
    {
        await Assert.That(actualRows.ValueKind).IsEqualTo(JsonValueKind.Array);
        using var registry = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(repositoryRoot, RegistryPath)));
        using var queryContract = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(repositoryRoot, QueryContractPath)));
        await AssertRegistryShapeAsync(registry.RootElement);
        var expectedRows = registry.RootElement.GetProperty(ContributorsProperty);
        await Assert.That(actualRows.GetArrayLength()).IsEqualTo(expectedRows.GetArrayLength());
        await AssertRegistryRowsMatchAsync(actualRows, expectedRows);
        await AssertRowsAsync(actualRows, ProductionSourceManifestAssertions.ProductionModules(repositoryRoot));
        await AssertQueryExpansionAsync(actualRows, queryContract.RootElement);
        await AssertAdditionalCandidatesAsync(actualRows, queryContract.RootElement);
    }

    private static async Task AssertRegistryShapeAsync(JsonElement registry)
    {
        await ProductionSourceManifestValueAssertions.FieldsAsync(registry, [SchemaVersionProperty, ContributorsProperty]);
        await Assert.That(registry.GetProperty(SchemaVersionProperty).GetInt32()).IsEqualTo(1);
        await Assert.That(registry.GetProperty(ContributorsProperty).ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    private static async Task AssertRegistryRowsMatchAsync(JsonElement actualRows, JsonElement expectedRows)
    {
        var actual = actualRows.EnumerateArray().ToArray();
        var expected = expectedRows.EnumerateArray().ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            await ProductionSourceManifestValueAssertions.FieldsAsync(actual[index], FieldNames);
            await ProductionSourceManifestValueAssertions.FieldsAsync(expected[index], FieldNames);
            foreach (var field in FieldNames)
            {
                await ProductionSourceManifestValueAssertions.EqualAsync(expected[index].GetProperty(field), actual[index].GetProperty(field));
            }
        }
    }

    private static async Task AssertRowsAsync(JsonElement contributors, HashSet<string> productionModules)
    {
        var suiteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contributor in contributors.EnumerateArray())
        {
            var suite = contributor.GetProperty(SuiteProperty).GetString()!;
            await Assert.That(Suites.Contains(suite, StringComparer.Ordinal)).IsTrue();
            suiteCounts[suite] = suiteCounts.GetValueOrDefault(suite) + 1;
            var className = contributor.GetProperty(ClassNameProperty).GetString()!;
            var methodName = contributor.GetProperty(MethodNameProperty).GetString()!;
            var instanceName = contributor.GetProperty(InstanceNameProperty).GetString()!;
            await Assert.That(className.Length).IsLessThanOrEqualTo(MaximumIdentityFieldCharacters);
            await Assert.That(methodName.Length).IsLessThanOrEqualTo(MaximumIdentityFieldCharacters);
            await Assert.That(instanceName.Length).IsLessThanOrEqualTo(MaximumIdentityFieldCharacters);
            await Assert.That(className).IsNotEmpty();
            await Assert.That(methodName).IsNotEmpty();
            await Assert.That(instanceName).IsNotEmpty();
            await Assert.That(identities.Add(Identity(suite, className, methodName, instanceName))).IsTrue();
            var operation = contributor.GetProperty(OperationProperty).GetString();
            await Assert.That(operation).IsNotEmpty();
            await Assert.That(operation!.Length).IsLessThanOrEqualTo(MaximumOperationCharacters);
            await AssertUniqueStringsAsync(contributor.GetProperty(RequirementsProperty), null);
            await AssertUniqueStringsAsync(contributor.GetProperty(AcceptanceProperty), null);
            await AssertUniqueStringsAsync(contributor.GetProperty(ExecutedModulesProperty), productionModules);
        }
        await Assert.That(suiteCounts.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(
            Suites.Order(StringComparer.Ordinal), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(suiteCounts[UnitSuite]).IsEqualTo(ExpectedUnitRows);
        await Assert.That(suiteCounts[RecoverySuite]).IsEqualTo(1);
        await Assert.That(suiteCounts[ClusterSuite]).IsEqualTo(ExpectedClusterRows);
    }

    private static async Task AssertQueryExpansionAsync(JsonElement contributors, JsonElement queryContract)
    {
        var cases = queryContract.GetProperty(ContributorsProperty).GetProperty(ExactCasesProperty).EnumerateArray().ToArray();
        var queryIdentities = ProductionSourceManifestQueryIdentities.Read(queryContract);
        foreach (var suite in new[] { UnitSuite })
        {
            var rows = contributors.EnumerateArray().Where(row =>
                row.GetProperty(SuiteProperty).GetString() == suite &&
                queryIdentities.Contains(ProductionSourceManifestQueryIdentities.Identity(row))).ToArray();
            await Assert.That(rows.Length).IsEqualTo(cases.Length);
            foreach (var sourceCase in cases)
            {
                var actual = rows.Single(row =>
                    row.GetProperty(ClassNameProperty).GetString() == sourceCase.GetProperty(ClassNameProperty).GetString() &&
                    row.GetProperty(MethodNameProperty).GetString() == sourceCase.GetProperty(MethodNameProperty).GetString() &&
                    row.GetProperty(InstanceNameProperty).GetString() == sourceCase.GetProperty(InstanceNameProperty).GetString());
                await Assert.That(actual.GetProperty(SuiteProperty).GetString()).IsEqualTo(suite);
                foreach (var field in FieldNames.Where(name => name != SuiteProperty && name != OperationProperty))
                {
                    await ProductionSourceManifestValueAssertions.EqualAsync(sourceCase.GetProperty(field), actual.GetProperty(field));
                }
                var methodName = sourceCase.GetProperty(MethodNameProperty).GetString()!;
                if (StrengthenedQueryOperations.TryGetValue(methodName, out var operation))
                {
                    await Assert.That(actual.GetProperty(OperationProperty).GetString()).IsEqualTo(operation);
                }
                else
                {
                    await ProductionSourceManifestValueAssertions.EqualAsync(sourceCase.GetProperty(OperationProperty), actual.GetProperty(OperationProperty));
                }
            }
        }
    }

    private static async Task AssertAdditionalCandidatesAsync(JsonElement contributors, JsonElement queryContract)
    {
        var queryIdentities = ProductionSourceManifestQueryIdentities.Read(queryContract);
        var candidates = contributors.EnumerateArray().Where(row =>
            !queryIdentities.Contains(ProductionSourceManifestQueryIdentities.Identity(row)))
            .Select(row => string.Join('|', row.GetProperty(SuiteProperty).GetString(),
                row.GetProperty(ClassNameProperty).GetString(), row.GetProperty(MethodNameProperty).GetString(),
                row.GetProperty(InstanceNameProperty).GetString())).Order(StringComparer.Ordinal);
        var expected = CandidateIdentities.Concat(ProductionSourceManifestExpansionIdentities.Values).Order(StringComparer.Ordinal);
        await Assert.That(candidates).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task AssertUniqueStringsAsync(JsonElement element, HashSet<string>? allowed)
    {
        await Assert.That(element.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(element.GetArrayLength()).IsGreaterThanOrEqualTo(1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            var value = item.GetString();
            await Assert.That(value).IsNotEmpty();
            await Assert.That(seen.Add(value!)).IsTrue();
            if (allowed is not null)
            {
                await Assert.That(allowed.Contains(value!)).IsTrue();
            }
        }
    }

    private static string Identity(string suite, string className, string methodName, string instanceName)
        => string.Concat(suite.Length, ":", suite, className.Length, ":", className,
            methodName.Length, ":", methodName, instanceName.Length, ":", instanceName);

}
