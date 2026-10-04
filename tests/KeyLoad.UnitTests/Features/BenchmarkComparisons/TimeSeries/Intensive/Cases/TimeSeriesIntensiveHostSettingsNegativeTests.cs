using KeyLoad.ComparisonHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using static KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive.TimeSeriesIntensiveHostSettingsFixture;
using static KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive.TimeSeriesIntensiveHostSettingsInvalidValues;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveHostSettingsNegativeTests
{
    [Test]
    [Arguments(RouteKey)]
    [Arguments(TargetKey)]
    [Arguments(NodesKey)]
    [Arguments(PhaseKey)]
    [Arguments(ProfileKey)]
    [Arguments(CellKey)]
    [Arguments(HashKey)]
    [Arguments(SourceKey)]
    [Arguments(ShaKey)]
    [Arguments(RunnerImageKey)]
    [Arguments(ServerImageKey)]
    [Arguments(JobKey)]
    [Arguments(RunKey)]
    [Arguments(AttemptKey)]
    [Arguments(RepositoryKey)]
    [Arguments(RefKey)]
    [Arguments(WorkflowKey)]
    [Arguments(OutputKey)]
    [Arguments(StorageKey)]
    [Arguments(ImageKey)]
    [Arguments(AdminKey)]
    [Arguments(IncarnationKey)]
    public async Task Th009002004MissingRequiredFactsRejectSafely(string key)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.KeyLoad);
        values.Remove(key);
        await RejectAsync(values);
    }

    [Test]
    [Arguments(RouteKey, Canary)]
    [Arguments(TargetKey, Canary)]
    [Arguments(NodesKey, Noncanonical)]
    [Arguments(PhaseKey, Canary)]
    [Arguments(ProfileKey, Canary)]
    [Arguments(ScenarioKey, nameof(TimeSeriesIntensiveScenario.Latest))]
    [Arguments(OldTargetKey, nameof(TimeSeriesIntensiveTargetKind.KeyLoad))]
    [Arguments(CellKey, WrongCell)]
    [Arguments(HashKey, OtherRevision)]
    [Arguments(SourceKey, OtherRevision)]
    [Arguments(ShaKey, OtherRevision)]
    [Arguments(SourceKey, Canary)]
    [Arguments(RunnerImageKey, Canary)]
    [Arguments(ServerImageKey, Canary)]
    [Arguments(ImageKey, TimescaleImage)]
    [Arguments(ImageKey, Canary)]
    [Arguments(JobKey, Zero)]
    [Arguments(JobKey, Noncanonical)]
    [Arguments(JobKey, Signed)]
    [Arguments(JobKey, Negative)]
    [Arguments(JobKey, Padded)]
    [Arguments(JobKey, Overflow)]
    [Arguments(RunKey, Zero)]
    [Arguments(AttemptKey, Zero)]
    [Arguments(RepositoryKey, Empty)]
    [Arguments(RefKey, Empty)]
    [Arguments(WorkflowKey, Empty)]
    [Arguments(OutputKey, RelativeOutput)]
    [Arguments(OutputKey, InvalidOutput)]
    [Arguments(StorageKey, OutputValue)]
    public async Task Th009002004DriftedSelectionIdentityCellImageAndJobRejectSafely(string key, string value)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.KeyLoad);
        values[key] = value;
        await RejectAsync(values);
    }

    internal static async Task RejectAsync(Dictionary<string, string?> values)
    {
        using var configuration = Configuration(values);
        InvalidOperationException? failure = null;
        try
        {
            _ = TimeSeriesIntensiveHostSettings.Read(configuration);
        }
        catch (InvalidOperationException actual)
        {
            failure = actual;
        }
        await Assert.That(failure).IsNotNull();
        var observed = failure ?? throw new InvalidOperationException(MissingFailure);
        await Assert.That(observed.Message).IsEqualTo(InvalidCode);
        await Assert.That(observed.InnerException).IsNull();
        await Assert.That(observed.ToString().Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(observed.ToString().Contains(ConnectionValue, StringComparison.Ordinal)).IsFalse();
    }
}

internal sealed class TimeSeriesIntensiveHostNativeSettingsNegativeTests
{
    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB)]
    public async Task Th009003ClosedNativeSectionRejectsScalarRootAndUnownedFields(TimeSeriesIntensiveTargetKind target)
    {
        foreach (var key in new[] { NativeRootKey, NativeUserKey, NativePasswordKey, NativeApiKey, NativeUnknownKey })
        {
            var values = Values(target);
            values[key] = Canary;
            await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
        }
    }

    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, ImageKey)]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, IncarnationKey)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB, ImageKey)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB, ConnectionKey)]
    public async Task Th009003ScalarNativeFieldsCannotHideNestedChildren(TimeSeriesIntensiveTargetKind target, string key)
    {
        var values = Values(target);
        values[key + NestedSuffix] = Canary;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
    }

    [Test]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, EndpointsKey)]
    [Arguments(TimeSeriesIntensiveTargetKind.KeyLoad, VotersKey)]
    [Arguments(TimeSeriesIntensiveTargetKind.TimescaleDB, EndpointsKey)]
    public async Task Th009003ObservableArrayShapesAndDuplicateAuthoritiesReject(TimeSeriesIntensiveTargetKind target, string section)
    {
        foreach (var shape in Enum.GetValues<InvalidArrayShape>())
        {
            var values = Values(target);
            ChangeArray(values, section, shape, target);
            await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
        }
    }

    [Test]
    [Arguments(InvalidHttpScheme)]
    [Arguments(UserInfoHttp)]
    [Arguments(PathHttp)]
    [Arguments(QueryHttp)]
    [Arguments(FragmentHttp)]
    [Arguments(WhitespaceHttp)]
    [Arguments(BackslashHttp)]
    [Arguments(Empty)]
    public async Task Th009003KeyLoadEndpointsAndVotersRequireOnlyAuthorities(string value)
    {
        foreach (var section in new[] { EndpointsKey, VotersKey })
        {
            var values = Values(TimeSeriesIntensiveTargetKind.KeyLoad);
            values[IndexKey(section, FirstIndex)] = value;
            await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
        }
    }

    [Test]
    [Arguments(IncarnationKey, EmptyGuid)]
    [Arguments(IncarnationKey, UpperGuid)]
    [Arguments(IncarnationKey, CompactGuid)]
    [Arguments(IncarnationKey, Canary)]
    [Arguments(AdminKey, Empty)]
    [Arguments(AdminKey, LineBreakCanary)]
    [Arguments(ConnectionKey, ConnectionValue)]
    [Arguments(ConnectionKey, Empty)]
    public async Task Th009003KeyLoadPrivateIdentityAndForeignCredentialsReject(string key, string value)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.KeyLoad);
        values[key] = value;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
    }

    [Test]
    [Arguments(NoTcpPort)]
    [Arguments(ZeroTcpPort)]
    [Arguments(OverflowTcpPort)]
    [Arguments(UserInfoTcp)]
    [Arguments(PathTcp)]
    [Arguments(QueryTcp)]
    [Arguments(FragmentTcp)]
    [Arguments(WhitespaceTcp)]
    [Arguments(UserInfoHttp)]
    public async Task Th009003TimescaleEndpointsRequireExplicitNativeTcpPort(string value)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.TimescaleDB);
        values[IndexKey(EndpointsKey, FirstIndex)] = value;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
    }

    [Test]
    [Arguments(AdminKey, Canary)]
    [Arguments(IncarnationKey, IncarnationValue)]
    [Arguments(VotersKey, Canary)]
    public async Task Th009003TimescaleRejectsEveryKeyLoadCredentialSection(string key, string value)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.TimescaleDB);
        values[key + NestedSuffix] = value;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
        values.Remove(key + NestedSuffix);
        values[key] = value;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
    }

    [Test]
    [Arguments(null)]
    [Arguments(Empty)]
    [Arguments(MalformedConnection)]
    [Arguments(MultiHostConnection)]
    [Arguments(ZeroPortConnection)]
    [Arguments(OverflowPortConnection)]
    [Arguments(NoHostConnection)]
    [Arguments(NoUserConnection)]
    [Arguments(NoPasswordConnection)]
    [Arguments(LineBreakConnection)]
    public async Task Th009003ActualNpgsqlParserAndPrivateFieldsHaveSafeFailure(string? value)
    {
        var values = Values(TimeSeriesIntensiveTargetKind.TimescaleDB);
        values[ConnectionKey] = value;
        await TimeSeriesIntensiveHostSettingsNegativeTests.RejectAsync(values);
    }

    private static void ChangeArray(Dictionary<string, string?> values, string section, InvalidArrayShape shape, TimeSeriesIntensiveTargetKind target)
    {
        var first = IndexKey(section, FirstIndex);
        var second = IndexKey(section, OneNode);
        switch (shape)
        {
            case InvalidArrayShape.Missing:
                values.Remove(second);
                break;
            case InvalidArrayShape.Scalar:
                values[section] = Canary;
                break;
            case InvalidArrayShape.Alias:
                values[section + AliasSuffix] = values[first];
                values.Remove(first);
                break;
            case InvalidArrayShape.Sparse:
                values[IndexKey(section, ThreeNodes)] = values[second];
                values.Remove(second);
                break;
            case InvalidArrayShape.Extra:
                values[IndexKey(section, TwoNodes)] = Canary;
                break;
            case InvalidArrayShape.Nested:
                values[first + NestedSuffix] = Canary;
                break;
            case InvalidArrayShape.Empty:
                values[first] = Empty;
                break;
            case InvalidArrayShape.Duplicate:
                values[second] = target == TimeSeriesIntensiveTargetKind.KeyLoad ? DuplicateHttp : DuplicateTcp;
                break;
        }
    }
}

internal enum InvalidArrayShape { Missing, Scalar, Alias, Sparse, Extra, Nested, Empty, Duplicate }

internal static class TimeSeriesIntensiveHostSettingsInvalidValues
{
    internal const string NativeRootKey = "Benchmarks:Native";
    internal const string NativeUserKey = NativeRootKey + ":User";
    internal const string NativePasswordKey = NativeRootKey + ":Password";
    internal const string NativeApiKey = NativeRootKey + ":ApiKey";
    internal const string NativeUnknownKey = NativeRootKey + ":NoSuchKey";
    internal const string Empty = "";
    internal const string Zero = "0";
    internal const string Noncanonical = "01";
    internal const string Signed = "+1";
    internal const string Negative = "-1";
    internal const string Padded = "1 ";
    internal const string Overflow = "9223372036854775808";
    internal const string RelativeOutput = "relative/PrivateTimeSeriesInputCanary";
    internal const string InvalidOutput = "/private/tmp/PrivateTimeSeriesInputCanary\0";
    internal const string WrongCell = "ts-keyload-n3-preflight";
    internal const string AliasSuffix = ":00";
    internal const string NestedSuffix = ":child";
    internal const string InvalidHttpScheme = "tcp://node1:8080";
    internal const string UserInfoHttp = "http://PrivateTimeSeriesInputCanary@node1:8080";
    internal const string PathHttp = "http://node1:8080/PrivateTimeSeriesInputCanary";
    internal const string QueryHttp = "http://node1:8080/?PrivateTimeSeriesInputCanary";
    internal const string FragmentHttp = "http://node1:8080/#PrivateTimeSeriesInputCanary";
    internal const string WhitespaceHttp = " http://node1:8080";
    internal const string BackslashHttp = "http:\\node1:8080";
    internal const string DuplicateHttp = "HTTP://NODE1:8080/";
    internal const string DuplicateTcp = "TCP://ISOLATED-TIMESCALE-1:5432/";
    internal const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
    internal const string UpperGuid = "E1CE6380-A182-4F84-A8C0-883A4CFB4ECD";
    internal const string CompactGuid = "e1ce6380a1824f84a8c0883a4cfb4ecd";
    internal const string LineBreakCanary = "PrivateTimeSeriesInputCanary\r\n";
    internal const string NoTcpPort = "tcp://native";
    internal const string ZeroTcpPort = "tcp://native:0";
    internal const string OverflowTcpPort = "tcp://native:65536";
    internal const string UserInfoTcp = "tcp://PrivateTimeSeriesInputCanary@native:5432";
    internal const string PathTcp = "tcp://native:5432/PrivateTimeSeriesInputCanary";
    internal const string QueryTcp = "tcp://native:5432/?PrivateTimeSeriesInputCanary";
    internal const string FragmentTcp = "tcp://native:5432/#PrivateTimeSeriesInputCanary";
    internal const string WhitespaceTcp = " tcp://native:5432";
    internal const string MalformedConnection = "PrivateTimeSeriesInputCanary=bad";
    internal const string MultiHostConnection = "Host=first,second;Username=user;Password=PrivateTimeSeriesInputCanary";
    internal const string ZeroPortConnection = "Host=native;Port=0;Username=user;Password=PrivateTimeSeriesInputCanary";
    internal const string OverflowPortConnection = "Host=native;Port=65536;Username=user;Password=PrivateTimeSeriesInputCanary";
    internal const string NoHostConnection = "Username=user;Password=PrivateTimeSeriesInputCanary";
    internal const string NoUserConnection = "Host=native;Password=PrivateTimeSeriesInputCanary";
    internal const string NoPasswordConnection = "Host=native;Username=user";
    internal const string LineBreakConnection = "Host=native;Username=user;Password='PrivateTimeSeriesInputCanary\r\n'";
}
