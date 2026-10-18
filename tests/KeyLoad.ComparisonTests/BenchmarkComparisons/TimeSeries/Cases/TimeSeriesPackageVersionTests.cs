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
    public async Task AcIso006PackageIdentityMatchesTheActuallyLoadedCentrallyPinnedPackage()
    {
        var source = XDocument.Load(Path.Combine(SessionNodeProcess.RepositoryRoot(), CentralPackages));
        var pin = source.Descendants(PackageVersion).Single(item => item.Attribute(Include)?.Value == Package)
            .Attribute(Version)!.Value;
        var actual = TimeSeriesComparisonPackageVersion.Read();
        await Assert.That(actual).IsEqualTo(pin);
    }
}
