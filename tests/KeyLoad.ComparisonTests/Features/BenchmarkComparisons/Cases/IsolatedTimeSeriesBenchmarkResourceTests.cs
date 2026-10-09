using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedTimeSeriesBenchmarkResourceTests
{
    private const string EnabledKey = "Benchmarks:Enabled";
    private const string RootKey = "Benchmarks:DataRoot";
    private const string OutputKey = "Benchmarks:Output";
    private const int ExpectedPreflightCells = 4;
    private const int ExpectedIntensiveCells = 20;
    private const string UnixPermissionsRequired = "This resource-model permission assertion requires Unix.";
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode SharedDirectoryMode = PrivateDirectoryMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    [Test]
    public async Task EveryClosedFamilySelectionComposesOnePrivateRunnerAndOnlyItsNativeGroup()
    {
        var contract = TimeSeriesIntensiveFamilyContract.Current;
        var cells = TimeSeriesIntensiveFamilyPlan.Create(contract);
        await Assert.That(cells.Preflight.Length).IsEqualTo(ExpectedPreflightCells);
        await Assert.That(cells.Intensive.Length).IsEqualTo(ExpectedIntensiveCells);
        foreach (var cell in cells.Preflight.Concat(cells.Intensive))
        {
            await IsolatedTimeSeriesBenchmarkResourceAssertions.VerifyCellAsync(cell, contract.ContractSha256);
        }
    }

    [Test]
    [Arguments("disabled")]
    [Arguments("missing-enabled")]
    [Arguments("mixed-general-selection")]
    [Arguments("invalid-time-series-selection")]
    [Arguments("contradictory-cell")]
    [Arguments("contradictory-contract")]
    [Arguments("existing-native-directory")]
    [Arguments("native-file")]
    public async Task InvalidOrMixedSelectionFailsBeforeResourcesOrDirectories(string invalidCase)
    {
        await using var model = new IsolatedTimeSeriesBenchmarkResourceModel();
        model.ConfigureSelection(TimeSeriesIntensiveFamilyPlan.Create(TimeSeriesIntensiveFamilyContract.Current)
            .Preflight[0]);
        model.Builder.Configuration.AddInMemoryCollection(
            InvalidConfiguration(invalidCase));
        if (invalidCase == MissingEnabledCase)
        {
            await Assert.That(model.Builder.Configuration[EnabledKey]).IsNull();
        }
        var native = PrepareExistingNativePath(model, invalidCase);
        var before = model.Builder.Resources.Count;
        await Assert.That(() => IsolatedTimeSeriesBenchmarkResources.Add(model.Builder))
            .Throws<InvalidOperationException>();
        await Assert.That(model.Builder.Resources.Count).IsEqualTo(before);
        await Assert.That(Directory.Exists(model.Output)).IsFalse();
        if (native is null)
        {
            await Assert.That(Directory.Exists(model.Root)).IsFalse();
        }
        else
        {
            await Assert.That(Directory.Exists(native) || File.Exists(native)).IsTrue();
            await Assert.That(Directory.GetFileSystemEntries(model.Root).Length).IsEqualTo(PreservedNativeEntryCount);
            var directory = Directory.Exists(native);
            var sentinel = directory ? Path.Combine(native, NativeSentinelName) : native;
            await Assert.That(await File.ReadAllTextAsync(sentinel,
                TestContext.Current!.Execution.CancellationToken)).IsEqualTo(NativeSentinelValue);
            if (directory)
            {
                await Assert.That(Directory.GetFileSystemEntries(native).Length).IsEqualTo(PreservedNativeEntryCount);
            }
        }
    }

    [Test]
    public async Task RelativeConfiguredRootOrOutputFailsBeforeAllocation()
    {
        foreach (var key in new[] { RootKey, OutputKey })
        {
            await using var model = new IsolatedTimeSeriesBenchmarkResourceModel();
            model.ConfigureSelection(TimeSeriesIntensiveFamilyPlan.Create(TimeSeriesIntensiveFamilyContract.Current)
                .Preflight[0]);
            model.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EnabledKey] = bool.TrueString,
                [key] = "relative/report-path"
            });
            var before = model.Builder.Resources.Count;
            await Assert.That(() => IsolatedTimeSeriesBenchmarkResources.Add(model.Builder))
                .Throws<InvalidOperationException>();
            await Assert.That(model.Builder.Resources.Count).IsEqualTo(before);
            await Assert.That(Directory.Exists(model.Root)).IsFalse();
        }
    }

    [Test]
    public async Task ExistingSharedOutputIsRejectedWithoutChangingItsPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(UnixPermissionsRequired);
        }
        await using var model = new IsolatedTimeSeriesBenchmarkResourceModel();
        model.ConfigureSelection(TimeSeriesIntensiveFamilyPlan.Create(TimeSeriesIntensiveFamilyContract.Current)
            .Preflight[0]);
        Directory.CreateDirectory(model.Root);
        File.SetUnixFileMode(model.Root, PrivateDirectoryMode);
        Directory.CreateDirectory(model.Output);
        File.SetUnixFileMode(model.Output, SharedDirectoryMode);
        model.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [EnabledKey] = bool.TrueString });
        var before = model.Builder.Resources.Count;

        await Assert.That(() => IsolatedTimeSeriesBenchmarkResources.Add(model.Builder))
            .Throws<InvalidOperationException>();
        await Assert.That(model.Builder.Resources.Count).IsEqualTo(before);
        await Assert.That(File.GetUnixFileMode(model.Output)).IsEqualTo(SharedDirectoryMode);
    }

    [Test]
    public async Task MissingPathsUseDistinctFreshPrivateRootAndReportsDirectory()
    {
        var first = await IsolatedTimeSeriesBenchmarkResourceAssertions.ComposeDefaultAsync();
        var second = await IsolatedTimeSeriesBenchmarkResourceAssertions.ComposeDefaultAsync();
        await Assert.That(first.Root).IsNotEqualTo(second.Root);
        await Assert.That(first.Output).IsEqualTo(Path.Combine(first.Root, "reports"));
        await Assert.That(second.Output).IsEqualTo(Path.Combine(second.Root, "reports"));
    }
    private const string TargetKey = "Benchmarks:TimeSeries:Target";
    private const string LegacyTargetKey = "Benchmarks:Target";
    private const string CellIdKey = "Benchmarks:TimeSeries:CellId";
    private const string ContractShaKey = "Benchmarks:TimeSeries:ContractSha256";
    private const string CellIdMismatch = "ts-keyload-n1-append";
    private const string HashMismatch = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string InvalidTarget = "Unknown";
    private const string KeyLoadTarget = "KeyLoad";
    private const string DisabledCase = "disabled";
    private const string MissingEnabledCase = "missing-enabled";
    private const string MixedCase = "mixed-general-selection";
    private const string InvalidSelectionCase = "invalid-time-series-selection";
    private const string CellMismatchCase = "contradictory-cell";
    private const string HashMismatchCase = "contradictory-contract";
    private const string ExistingNativeDirectoryCase = "existing-native-directory";
    private const string NativeFileCase = "native-file";
    private const string NativeDirectoryName = "native";
    private const string NativeSentinelName = "existing-cell-sentinel";
    private const string NativeSentinelValue = "preserve-existing-native-content";
    private const int PreservedNativeEntryCount = 1;
    private static Dictionary<string, string?> InvalidConfiguration(string invalidCase)
    {
        var settings = new Dictionary<string, string?>
        {
            [EnabledKey] = bool.TrueString
        };
        switch (invalidCase)
        {
            case DisabledCase:
                settings[EnabledKey] = bool.FalseString;
                break;
            case MissingEnabledCase:
                settings.Remove(EnabledKey);
                break;
            case MixedCase:
                settings[LegacyTargetKey] = KeyLoadTarget;
                break;
            case InvalidSelectionCase:
                settings[TargetKey] = InvalidTarget;
                break;
            case CellMismatchCase:
                settings[CellIdKey] = CellIdMismatch;
                break;
            case HashMismatchCase:
                settings[ContractShaKey] = HashMismatch;
                break;
        }
        return settings;
    }
    private static string? PrepareExistingNativePath(IsolatedTimeSeriesBenchmarkResourceModel model, string invalidCase)
    {
        var native = Path.Combine(model.Root, NativeDirectoryName);
        if (invalidCase == ExistingNativeDirectoryCase)
        {
            Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, NativeSentinelName), NativeSentinelValue);
            return native;
        }
        if (invalidCase == NativeFileCase)
        {
            Directory.CreateDirectory(model.Root);
            File.WriteAllText(native, NativeSentinelValue);
            return native;
        }
        return null;
    }
}

internal static class IsolatedTimeSeriesBenchmarkResourceAssertions
{
    private const string EnabledKey = "Benchmarks:Enabled";
    private const string RouteEnvironment = "Benchmarks__Profile";
    private const string TargetEnvironment = "Benchmarks__TimeSeries__Target";
    private const string NodeCountEnvironment = "Benchmarks__TimeSeries__NodeCount";
    private const string PhaseEnvironment = "Benchmarks__TimeSeries__Phase";
    private const string ScenarioEnvironment = "Benchmarks__TimeSeries__Scenario";
    private const string EvidenceProfileEnvironment = "Benchmarks__TimeSeries__EvidenceProfile";
    private const string CellIdEnvironment = "Benchmarks__TimeSeries__CellId";
    private const string ContractHashEnvironment = "Benchmarks__TimeSeries__ContractSha256";
    private const string StorageKey = "Benchmarks__Storage";
    private const string NativePrefix = "Benchmarks__Native__";
    private const string NativeImageEnvironment = NativePrefix + "Image";
    private const string NativeEndpointsPrefix = NativePrefix + "Endpoints__";
    private const string AdminEnvironment = "Benchmarks__AdminKey";
    private const string AdminExpression = "{admin-key.value}";
    private const string NodePeerPrefix = "KeyLoad__Peers__";
    private const string ExpectedServerDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ExpectedContractSha = "9b13ab99e8acb25ad11d08e394fd5af758553ae00bef0a52a858975ea58a5fbb";
    private const string ExpectedStorage = "Fresh TimeSeries cell-owned native directories; no shared database";
    private const string RunnerName = "comparisons";
    private const string KeyLoadNodePrefix = "node";
    private const string TimescaleNodePrefix = "isolated-timescale-";
    private const string DataTarget = "/data";
    private const string ReportsTarget = "/reports";
    private const string IntensiveRoute = "timeseries-intensive";
    private const string ExpectedEvidenceProfile = "intensive-timeseries-4096-c16";
    private const string KeyLoadDataDirectory = "keyload";
    private const int NativeCountOffset = 1;
    internal static async Task VerifyCellAsync(TimeSeriesIntensiveFamilyCell cell, string contractSha)
    {
        await using var model = new IsolatedTimeSeriesBenchmarkResourceModel();
        model.ConfigureSelection(cell);
        model.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [EnabledKey] = bool.TrueString });
        IsolatedTimeSeriesBenchmarkResources.Add(model.Builder);
        var resources = model.Build();
        var runner = resources.OfType<ContainerResource>().Single(resource => resource.Name == RunnerName);
        var nativePrefix = cell.Selection.Target == TimeSeriesIntensiveTargetKind.KeyLoad
            ? KeyLoadNodePrefix : TimescaleNodePrefix;
        var nodes = resources.OfType<ContainerResource>()
            .Where(resource => resource.Name.StartsWith(nativePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(resources.Length).IsEqualTo(cell.Selection.NodeCount + NativeCountOffset);
        await Assert.That(nodes.Length).IsEqualTo(cell.Selection.NodeCount);
        await Assert.That(Directory.Exists(model.Root)).IsTrue();
        await Assert.That(Directory.Exists(model.Output)).IsTrue();
        await Assert.That(Path.IsPathFullyQualified(model.Root)).IsTrue();
        await Assert.That(Path.IsPathFullyQualified(model.Output)).IsTrue();
        await VerifyPrivateDirectoryAsync(model.Root);
        await VerifyPrivateDirectoryAsync(model.Output);
        await VerifyRunnerAsync(runner, cell, contractSha, model.Output, nodes);
        await VerifyNativeAsync(cell, model, runner, nodes);
    }
    private static async Task VerifyRunnerAsync(ContainerResource runner, TimeSeriesIntensiveFamilyCell cell,
        string contractSha, string output, ContainerResource[] nodes)
    {
        var config = await IsolatedResourceTopologyFixture.ConfigurationAsync(runner);
        var environment = config.EnvironmentVariables.ToDictionary();
        await Assert.That(environment[RouteEnvironment]).IsEqualTo(IntensiveRoute);
        await Assert.That(environment[TargetEnvironment]).IsEqualTo(cell.Selection.Target.ToString());
        await Assert.That(environment[NodeCountEnvironment])
            .IsEqualTo(cell.Selection.NodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(environment[PhaseEnvironment]).IsEqualTo(cell.Selection.Phase.ToString());
        await Assert.That(environment.ContainsKey(ScenarioEnvironment))
            .IsEqualTo(cell.Selection.Phase == TimeSeriesIntensiveCellPhase.Intensive);
        if (cell.Selection.Scenario is { } scenario)
        {
            await Assert.That(environment[ScenarioEnvironment]).IsEqualTo(scenario.ToString());
        }
        await Assert.That(environment[EvidenceProfileEnvironment]).IsEqualTo(ExpectedEvidenceProfile);
        await Assert.That(environment[CellIdEnvironment]).IsEqualTo(cell.Id);
        await Assert.That(environment[ContractHashEnvironment]).IsEqualTo(contractSha);
        await Assert.That(contractSha).IsEqualTo(ExpectedContractSha);
        await Assert.That(environment[StorageKey]).IsEqualTo(ExpectedStorage);
        await Assert.That(runner.Annotations.OfType<ContainerMountAnnotation>()
            .Single(mount => mount.Target == ReportsTarget).Source).IsEqualTo(output);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(NativeEndpointsPrefix, StringComparison.Ordinal)))
            .IsEqualTo(nodes.Length);
    }
    private static async Task VerifyNativeAsync(TimeSeriesIntensiveFamilyCell cell,
        IsolatedTimeSeriesBenchmarkResourceModel model, ContainerResource runner, ContainerResource[] nodes)
    {
        if (cell.Selection.Target == TimeSeriesIntensiveTargetKind.TimescaleDB)
        {
            await VerifyTimescaleAsync(model, runner, nodes, cell.Selection.NodeCount);
            return;
        }
        await VerifyKeyLoadAsync(model, runner, nodes, cell.Selection.NodeCount);
    }
    private static async Task VerifyTimescaleAsync(IsolatedTimeSeriesBenchmarkResourceModel model,
        ContainerResource runner, ContainerResource[] nodes, int count)
    {
        await Assert.That(nodes.Select(node => node.Name)).IsEquivalentTo(
            Enumerable.Range(NativeCountOffset, count).Select(index => TimescaleNodePrefix + index));
        foreach (var node in nodes)
        {
            await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyNodeAsync(model.Root, node,
                Array.IndexOf(nodes, node), count);
        }
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyUniqueNativePlacementAsync(nodes, count);
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifySharedCredentialAsync(model.Builder, nodes);
        await IsolatedTimeSeriesTimescaleResourceAssertions.VerifyRunnerBindingsAsync(runner, model.Builder, nodes, count);
    }
    private static async Task VerifyKeyLoadAsync(IsolatedTimeSeriesBenchmarkResourceModel model,
        ContainerResource runner, ContainerResource[] nodes, int count)
    {
        await Assert.That(nodes.Select(node => node.Name)).IsEquivalentTo(
            Enumerable.Range(NativeCountOffset, count).Select(index => KeyLoadNodePrefix + index));
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
        foreach (var node in nodes)
        {
            await IsolatedResourceTopologyFixture.VerifyPrivateDataAsync(node, model.Root, KeyLoadDataDirectory);
            await IsolatedResourceTopologyFixture.VerifyUserAsync(node);
            var nodeEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(node.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256)
                .IsEqualTo(ExpectedServerDigest);
            await Assert.That(node.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias)
                .IsEqualTo(node.Name);
            await Assert.That(nodeEnvironment.Keys.Count(key => key.StartsWith(NodePeerPrefix, StringComparison.Ordinal)))
                .IsEqualTo(count);
            for (var index = 0; index < count; index++)
            {
                var peer = NodePeerPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var origin = "http://node" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":8080";
                await Assert.That(nodeEnvironment[peer]).IsEqualTo(origin);
            }
        }
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(runner);
        await Assert.That(environment[NativeImageEnvironment]).IsEqualTo(IsolatedResourceTopologyFixture.ServerImage);
        await Assert.That(environment[AdminEnvironment]).IsEqualTo(AdminExpression);
        await Assert.That(environment.Keys.Count(key => key.StartsWith(NativeEndpointsPrefix, StringComparison.Ordinal)))
            .IsEqualTo(count);
        for (var index = 0; index < count; index++)
        {
            var endpoint = NativeEndpointsPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var expected = "{node" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bindings.http.url}";
            await Assert.That(environment[endpoint]).IsEqualTo(expected);
        }
    }
    internal static async Task<(string Root, string Output)> ComposeDefaultAsync()
    {
        await using var model = new IsolatedTimeSeriesBenchmarkResourceModel(configurePaths: false);
        var cell = TimeSeriesIntensiveFamilyPlan.Create(TimeSeriesIntensiveFamilyContract.Current).Preflight[0];
        model.ConfigureSelection(cell, includeIdentity: false);
        model.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [EnabledKey] = bool.TrueString });
        IsolatedTimeSeriesBenchmarkResources.Add(model.Builder);
        var native = model.Builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name.StartsWith(
            KeyLoadNodePrefix, StringComparison.Ordinal));
        var data = native.Annotations.OfType<ContainerMountAnnotation>().Single(mount => mount.Target == DataTarget).Source!;
        var root = Directory.GetParent(Directory.GetParent(Directory.GetParent(data)!.FullName)!.FullName)!.FullName;
        model.SetRootForCleanup(root);
        var resources = model.Build();
        var runner = resources.OfType<ContainerResource>().Single(resource => resource.Name == RunnerName);
        var environment = (await IsolatedResourceTopologyFixture.ConfigurationAsync(runner)).EnvironmentVariables.ToDictionary();
        var reports = runner.Annotations.OfType<ContainerMountAnnotation>().Single(mount => mount.Target == ReportsTarget).Source!;
        await Assert.That(reports).IsEqualTo(Path.Combine(root, "reports"));
        await Assert.That(environment[CellIdEnvironment]).IsEqualTo(cell.Id);
        await Assert.That(environment[ContractHashEnvironment]).IsEqualTo(ExpectedContractSha);
        await VerifyPrivateDirectoryAsync(root);
        await VerifyPrivateDirectoryAsync(reports);
        return (root, reports);
    }
    private static async Task VerifyPrivateDirectoryAsync(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
