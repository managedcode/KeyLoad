using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class NativeTUnitSelectionTests
{
    private const string ArgumentsProperty = "args";
    private const string EnvironmentProperty = "environment";
    private const string SelectionProperty = "selected";
    private const string RejectionProperty = "rejection";
    private const string UnsupportedRejectionCountProperty = "rejectionUnsupportedCount";
    private const string RejectionFilter = "/*/*/RelationalSqlClusterJoinRejectionTests/*";
    private const string StandardProperty = "standard";
    private const int ExpectedRetainedSnapshotRejections = 4;
    private const string RetainedSnapshotProperty = "retainedSnapshot";
    private const string RetainedSnapshotUnsupportedProperty = "retainedSnapshotUnsupportedCount";
    private const string RetainedSnapshotFilter = "/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes";
    private const string NativeTextProperty = "nativeText";
    private const string NativeTextUnsupportedCountProperty = "nativeTextUnsupportedCount";
    private const int ExpectedNativeTextRejectionCount = 4;
    private const string NativeTextFilter = "/*/*/(NativeTextAsyncClusterTests)|(NativeTextClusterLeaderLossTests)|(NativeTextClusterTests)|(NativeTextWaitClusterTests)/*";
    private const string GraphIncomingProperty = "graphIncoming";
    private const string GraphIncomingUnsupportedProperty = "graphIncomingUnsupportedCount";
    private const string GraphIncomingFilter = "/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit";
    private const int ExpectedGraphIncomingRejections = 4;
    private const string LocalArgumentsEnvironment = "KEYLOAD_TUNIT_LOCAL_CLUSTER_IMAGE_ARGUMENTS";
    private const string LocalEnabledEnvironment = "KeyLoadTests__LocalClusterImage__Enabled";
    private const string LocalProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string LocalReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string LocalReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string LocalChildEnvironment = "KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD";
    private const string StandardFilter = "/*/*/(PartitionQueryMcpSchemaTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*";
    private const string RevisionEnvironment = "GITHUB_SHA";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string IntrinsicEnvironment = "DOTNET_EnableHWIntrinsic";
    private const string ParallelismArgument = "--maximum-parallel-tests";
    private const string ExpectedDefaultParallelism = "50";
    private const string ExpectedExplicit20Parallelism = "20";
    private const string Explicit20Property = "explicit20";
    private const string Tuned50Property = "tuned50";
    private const string ExpectedTunedParallelism = "50";

    [Test]
    [Arguments("unit", "KeyLoad.UnitTests")]
    [Arguments("analyzers", "KeyLoad.Analyzers.Tests")]
    [Arguments("recovery", "KeyLoad.RecoveryTests")]
    [Arguments("cluster", "KeyLoad.IntegrationTests")]
    public async Task NativeEntryPreservesOriginalTUnitArgumentsAndRejectsInvalidSelections(string suite, string project)
    {
        using var selection = JsonDocument.Parse(await NativeTestSelectionProcess.ReadAsync(suite).ConfigureAwait(false));
        var selected = selection.RootElement.GetProperty(SelectionProperty);
        var args = selected.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        var environment = selected.GetProperty(EnvironmentProperty);
        await Assert.That(args[0]).IsEqualTo("test");
        await Assert.That(args[2]).IsEqualTo("tests/" + project);
        await Assert.That(args[Array.IndexOf(args, "--output") + 1]).IsEqualTo("Detailed");
        await Assert.That(args[Array.IndexOf(args, ParallelismArgument) + 1]).IsEqualTo(ExpectedDefaultParallelism);
        await AssertParallelismAsync(selection.RootElement.GetProperty(Explicit20Property), ExpectedExplicit20Parallelism)
            .ConfigureAwait(false);
        await AssertParallelismAsync(selection.RootElement.GetProperty(Tuned50Property), ExpectedTunedParallelism)
            .ConfigureAwait(false);
        await Assert.That(args[Array.IndexOf(args, "--treenode-filter") + 1]).IsEqualTo("/*/*/ActualCase/*");
        await Assert.That(args).Contains("--report-trx");
        await Assert.That(args).Contains("--coverage");
        await Assert.That(args).DoesNotContain("build/KeyLoad.AppHost");
        await Assert.That(environment.GetProperty(RevisionEnvironment).GetString()).IsEqualTo("original-revision");
        await Assert.That(environment.TryGetProperty(SuiteEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalArgumentsEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(IntrinsicEnvironment, out _)).IsFalse();
        if (suite == "cluster")
        {
            await AssertLocalSelectorAsync(selection.RootElement.GetProperty(StandardProperty), StandardFilter).ConfigureAwait(false);
            await AssertLocalSelectorAsync(selection.RootElement.GetProperty(RejectionProperty), RejectionFilter).ConfigureAwait(false);
            await AssertLocalSelectorAsync(selection.RootElement.GetProperty(NativeTextProperty), NativeTextFilter).ConfigureAwait(false);
            await AssertRetainedSnapshotAsync(selection.RootElement).ConfigureAwait(false);
            await NativeResourcePolicySelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await NativeMcpAuthoritySelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await NativeR33PublicSelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await NativeR34PublicSelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await NativeR34PublicSelectionAssertions.AssertSuccessorAsync(selection.RootElement).ConfigureAwait(false);
            await NativePublicCohortSelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await NativeCurrentModelSelectionAssertions.AssertAsync(selection.RootElement).ConfigureAwait(false);
            await AssertLocalSelectorAsync(selection.RootElement.GetProperty(GraphIncomingProperty), GraphIncomingFilter).ConfigureAwait(false);
            await Assert.That(selection.RootElement.GetProperty(GraphIncomingUnsupportedProperty).GetInt32())
                .IsEqualTo(ExpectedGraphIncomingRejections);
            await Assert.That(selection.RootElement.GetProperty(NativeTextUnsupportedCountProperty).GetInt32())
                .IsEqualTo(ExpectedNativeTextRejectionCount);
            await Assert.That(selection.RootElement.GetProperty(UnsupportedRejectionCountProperty).GetInt32()).IsEqualTo(4);
        }
    }

    private static async Task AssertRetainedSnapshotAsync(JsonElement root)
    {
        await AssertLocalSelectorAsync(root.GetProperty(RetainedSnapshotProperty), RetainedSnapshotFilter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RetainedSnapshotUnsupportedProperty).GetInt32())
            .IsEqualTo(ExpectedRetainedSnapshotRejections);
    }

    private static async Task AssertParallelismAsync(JsonElement selection, string expected)
    {
        var arguments = selection.GetProperty(ArgumentsProperty).EnumerateArray().Select(value => value.GetString()).ToArray();
        await Assert.That(arguments[Array.IndexOf(arguments, ParallelismArgument) + 1]).IsEqualTo(expected);
    }

    internal static async Task AssertLocalSelectorAsync(JsonElement local, string expectedFilter)
    {
        var environment = local.GetProperty(EnvironmentProperty);
        using var arguments = JsonDocument.Parse(environment.GetProperty(LocalArgumentsEnvironment).GetString()!);
        var localTUnitArguments = local.GetProperty(ArgumentsProperty).EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        var filterIndex = Array.IndexOf(localTUnitArguments, "--treenode-filter");
        await Assert.That(localTUnitArguments[Array.IndexOf(localTUnitArguments, ParallelismArgument) + 1])
            .IsEqualTo(ExpectedDefaultParallelism);
        await Assert.That(filterIndex >= 0).IsTrue();
        await Assert.That(localTUnitArguments[filterIndex + 1]).IsEqualTo(expectedFilter);
        var localArgs = arguments.RootElement.EnumerateArray().Select(value => value.GetString()!).ToArray();
        await Assert.That(localArgs).IsEquivalentTo([
            "--KeyLoadTests:Suite=cluster", "--KeyLoadTests:Filter=" + expectedFilter,
            "--KeyLoadTests:LocalClusterImage:Enabled=true"], CollectionOrdering.Matching);
        await Assert.That(environment.TryGetProperty(RevisionEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalArgumentsEnvironment, out _)).IsTrue();
        await Assert.That(environment.TryGetProperty(LocalEnabledEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalProvenanceEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalReceiptEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalReferenceEnvironment, out _)).IsFalse();
        await Assert.That(environment.TryGetProperty(LocalChildEnvironment, out _)).IsFalse();
    }
}
