using System.Xml.Linq;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesPackageVersionTests
{
    private const string CentralPackages = "Directory.Packages.props";
    private const string PackageVersion = "PackageVersion";
    private const string Include = "Include";
    private const string Version = "Version";
    private const string Package = "ManagedCode.TimeSeries";

    [Test]
    public async Task AcIso006ReportedLibraryVersionMatchesTheActuallyLoadedCentrallyPinnedPackage()
    {
        var source = XDocument.Load(Path.Combine(ImageBundleRealProtocol.RepositoryRoot(), CentralPackages));
        var pin = source.Descendants(PackageVersion).Single(item => item.Attribute(Include)?.Value == Package)
            .Attribute(Version)!.Value;
        var recorder = new TimeSeriesComparisonRecorder(TimeSeriesComparisonWorkloadFactory.Create("version-proof"), TimeProvider.System);
        recorder.AddLibraryTarget();
        var report = recorder.CreateReport(new string('a', 40));

        await Assert.That(report.Targets.Single().PackageVersion).IsEqualTo(pin);
    }
}
