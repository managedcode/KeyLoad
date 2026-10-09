using KeyLoad.ComparisonHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using static KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive.TimeSeriesIntensiveHostSettingsFixture;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveHostSettingsTests
{
    /// <summary>TH009002/003/004: all24 source-plan selections retain their actual typed input facts.</summary>
    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, OneNode)]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, ThreeNodes)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB, OneNode)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB, ThreeNodes)]
    public async Task AllNativeSelectionsAndEveryPhaseScenarioRetainOriginalSettings(TimeSeriesIntensiveTargetKind target, int count)
    {
        await VerifySelectionAsync(target, count, null);
        foreach (var scenario in new[] { TimeSeriesIntensiveScenario.Append, TimeSeriesIntensiveScenario.RawRangeRead,
            TimeSeriesIntensiveScenario.Latest, TimeSeriesIntensiveScenario.Aggregate, TimeSeriesIntensiveScenario.Windows })
        {
            await VerifySelectionAsync(target, count, scenario);
        }
    }

    [Test]
    public async Task PrivateRunIdIsFreshCanonicalGuidAndIndependentOfGitHubRun()
    {
        using var configuration = Configuration(Values(TimeSeriesIntensiveTargetKind.KeyLoad));
        var first = TimeSeriesIntensiveHostSettings.Read(configuration);
        var second = TimeSeriesIntensiveHostSettings.Read(configuration);
        await Assert.That(Guid.TryParseExact(first.RunId, GuidN, out var actual)).IsTrue();
        await Assert.That(actual).IsNotEqualTo(Guid.Empty);
        await Assert.That(first.RunId.Length).IsEqualTo(GuidLength);
        await Assert.That(first.RunId).IsEqualTo(actual.ToString(GuidN));
        await Assert.That(first.RunId).IsNotEqualTo(second.RunId);
        await Assert.That(first.RunId).IsNotEqualTo(RunText);
        await Assert.That(first.Identity.Provenance.RunId).IsEqualTo(RunNumber);
    }

    /// <summary>TH009003: generated positional-record diagnostics cannot reveal private fields.</summary>
    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB)]
    public async Task RecordDiagnosticsAreFixedWhileNativeSecretsRemainAvailable(TimeSeriesIntensiveTargetKind target)
    {
        using var configuration = Configuration(Values(target));
        var result = TimeSeriesIntensiveHostSettings.Read(configuration);
        await Assert.That(result.ToString()).IsEqualTo(SettingsName);
        await Assert.That(result.Native.ToString()).IsEqualTo(NativeName);
        await Assert.That(result.ToString().Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Native.ToString().Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(target == TimeSeriesIntensiveTargetKind.KeyLoad ? result.Native.AdminKey : result.Native.ConnectionString)
            .IsEqualTo(target == TimeSeriesIntensiveTargetKind.KeyLoad ? Canary : ConnectionValue);
    }

    [Test]
    public async Task NullConfigurationPreservesArgumentNullBoundary()
    {
        await Assert.That(() => TimeSeriesIntensiveHostSettings.Read(null!)).Throws<ArgumentNullException>();
        var selection = new TimeSeriesIntensiveSelection(TimeSeriesIntensiveTargetKind.KeyLoad, OneNode,
            TimeSeriesIntensiveCellPhase.Preflight, null, ProfileValue);
        await Assert.That(() => TimeSeriesIntensiveHostNativeSettings.Read(null!, selection, ServerImage))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HttpsEndpointAndVoterAuthoritiesRetainTheirOriginalText()
    {
        var values = Values(TimeSeriesIntensiveTargetKind.KeyLoad);
        values[IndexKey(EndpointsKey, FirstIndex)] = HttpsOrigin;
        values[IndexKey(VotersKey, FirstIndex)] = HttpsOrigin;
        using var configuration = Configuration(values);
        var result = TimeSeriesIntensiveHostSettings.Read(configuration);
        await Assert.That(result.Native.Endpoints[FirstIndex].OriginalString).IsEqualTo(HttpsOrigin);
        await Assert.That(result.Native.VoterIds[FirstIndex]).IsEqualTo(HttpsOrigin);
    }

    [Test]
    [Arguments(MinimumTcpEndpoint, MinimumPortConnection, MinimumPort)]
    [Arguments(MaximumTcpEndpoint, MaximumPortConnection, MaximumPort)]
    public async Task NativePortBoundariesAndActualQuotedNpgsqlPasswordRemainPrivate(string endpoint, string connection, int port)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.TimescaleDB);
        values[IndexKey(EndpointsKey, FirstIndex)] = endpoint;
        values[ConnectionKey] = connection;
        using var configuration = Configuration(values);
        var result = TimeSeriesIntensiveHostSettings.Read(configuration);
        await Assert.That(result.Native.Endpoints[FirstIndex].Port).IsEqualTo(port);
        await Assert.That(result.Native.ConnectionString).IsEqualTo(connection);
        await Assert.That(result.Native.ToString()).IsEqualTo(NativeName);
    }

    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB)]
    public async Task NativeFieldNamesFollowActualMergedConfigurationCaseSemantics(TimeSeriesIntensiveTargetKind target)
    {
        var values = Values(target);
        foreach (var key in values.Keys.Where(key => key.StartsWith(TimeSeriesIntensiveHostSettingsInvalidValues.NativeRootKey,
            StringComparison.Ordinal)).ToArray())
        {
            var value = values[key];
            values.Remove(key);
            values[key.ToUpperInvariant()] = value;
        }
        using var configuration = Configuration(values);
        await Assert.That(TimeSeriesIntensiveHostSettings.Read(configuration).Native.Endpoints.Length).IsEqualTo(ThreeNodes);
    }

    private static async Task VerifySelectionAsync(TimeSeriesIntensiveTargetKind target, int count, TimeSeriesIntensiveScenario? scenario)
    {
        using var configuration = Configuration(Values(target, count, scenario));
        var result = TimeSeriesIntensiveHostSettings.Read(configuration);
        await Assert.That(result.Selection.Target).IsEqualTo(target);
        await Assert.That(result.Selection.NodeCount).IsEqualTo(count);
        await Assert.That(result.Selection.Scenario).IsEqualTo(scenario);
        await Assert.That(result.Selection.Phase).IsEqualTo(scenario is null
            ? TimeSeriesIntensiveCellPhase.Preflight : TimeSeriesIntensiveCellPhase.Intensive);
        await Assert.That(result.Cell.Id).IsEqualTo(CellId(target, count, scenario));
        await Assert.That(result.Cell.Selection).IsEqualTo(result.Selection);
        await Assert.That(result.ContractSha256).IsEqualTo(ContractHash);
        await Assert.That(result.JobId).IsEqualTo(JobNumber);
        await Assert.That(result.OutputDirectory).IsEqualTo(OutputValue);
        await Assert.That(result.Storage).IsEqualTo(StorageValue);
        await Assert.That(result.Identity.KeyLoadImage).IsEqualTo(ServerImage);
        await Assert.That(result.Identity.LoadGeneratorImage).IsEqualTo(RunnerImage);
        await Assert.That(result.Identity.Provenance).IsEqualTo(new KeyLoad.Comparisons.GitHubProvenance(
            RunNumber, AttemptNumber, RepositoryValue, RefValue, WorkflowValue, ProfileValue));
        await VerifyNativeAsync(result, target, count);
    }

    private static async Task VerifyNativeAsync(TimeSeriesIntensiveHostSettings result, TimeSeriesIntensiveTargetKind target, int count)
    {
        await Assert.That(result.Image).IsEqualTo(target == TimeSeriesIntensiveTargetKind.KeyLoad ? ServerImage : TimescaleImage);
        await Assert.That(result.Native.Image).IsEqualTo(result.Image);
        await Assert.That(result.Native.Endpoints.Length).IsEqualTo(count);
        for (var index = FirstIndex; index < count; index++)
        {
            await Assert.That(result.Native.Endpoints[index].OriginalString).IsEqualTo(Endpoint(target, index));
        }
        if (target == TimeSeriesIntensiveTargetKind.KeyLoad)
        {
            await Assert.That(result.Native.VoterIds.Length).IsEqualTo(count);
            for (var index = FirstIndex; index < count; index++)
            {
                await Assert.That(result.Native.VoterIds[index]).IsEqualTo(Voter(index));
            }
            await Assert.That(result.Native.Incarnation).IsEqualTo(Guid.ParseExact(IncarnationValue, GuidD));
            await Assert.That(result.Native.AdminKey).IsEqualTo(Canary);
            await Assert.That(result.Native.ConnectionString).IsNull();
            return;
        }
        await Assert.That(result.Native.VoterIds.IsEmpty).IsTrue();
        await Assert.That(result.Native.Incarnation).IsNull();
        await Assert.That(result.Native.AdminKey).IsNull();
        await Assert.That(result.Native.ConnectionString).IsEqualTo(ConnectionValue);
    }
}
