using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class TimeSeriesHttpAdmissionTests
{
    private const string LatestRoute = "/v1/series/latest";
    private const string AggregateRoute = "/v1/series/aggregate";
    private const string WindowsRoute = "/V1/SERIES/WINDOWS/";
    private const string ExistingQueryRoute = "/v1/query";
    private const int HeavyReadBytes = 16_384;
    private const int ReservedReadBytes = 20_480;
    private const int BodyBytes = 1_024;
    private const int ControlBodyBytes = 256;
    private const int NodeCapacity = 25_000;
    private const int ControlCapacity = 20_000;
    private const int RequestCapacity = 16;
    private const int ControlRequests = 2;

    [Test]
    [Arguments(LatestRoute)]
    [Arguments(AggregateRoute)]
    [Arguments(WindowsRoute)]
    public async Task AcSeries011NewReadsReserveTheSharedHeavyWorkingSetAndReleaseAfterRejection(string route)
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(new()
        {
            MaxRequests = RequestCapacity,
            MaxReservedBytes = NodeCapacity,
            OtherReservedBytes = 0,
            HeavyReadReservedBytes = HeavyReadBytes,
            MaxBodyBytes = BodyBytes,
            MaxControlBodyBytes = ControlBodyBytes,
            ReservedControlRequests = ControlRequests,
            ReservedControlBytes = ControlCapacity
        }));
        using var query = governor.Begin(ExistingQueryRoute, 0);
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(ReservedReadBytes);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => governor.Begin(route, 0));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(1);
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(ReservedReadBytes);
        query.Dispose();
        using var admitted = governor.Begin(route, 0);
        await Assert.That(admitted.MaxBodyBytes).IsEqualTo(BodyBytes);
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(ReservedReadBytes);
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(0);
        admitted.Dispose();
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(0);
    }
}
