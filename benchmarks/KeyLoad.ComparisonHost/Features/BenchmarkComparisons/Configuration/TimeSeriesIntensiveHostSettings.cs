using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal sealed record TimeSeriesIntensiveHostSettings(TimeSeriesIntensiveSelection Selection,
    TimeSeriesIntensiveFamilyCell Cell, string ContractSha256, ComparisonExecutionIdentity Identity,
    long JobId, string Image, string OutputDirectory, string Storage, string RunId,
    TimeSeriesIntensiveHostNativeSettings Native)
{
    public override string ToString() => nameof(TimeSeriesIntensiveHostSettings);

    internal static TimeSeriesIntensiveHostSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            return ReadValidated(configuration);
        }
        catch (InvalidOperationException)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
        catch (ArgumentException)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
        catch (PathTooLongException)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
    }

    private static TimeSeriesIntensiveHostSettings ReadValidated(IConfiguration configuration)
    {
        var selection = TimeSeriesIntensiveSelection.Read(configuration);
        var contract = TimeSeriesIntensiveFamilyContract.Current;
        var plan = TimeSeriesIntensiveFamilyPlan.Create(contract);
        var cell = plan.Preflight.Concat(plan.Intensive).Single(item => item.Selection == selection);
        if (TimeSeriesIntensiveHostInput.Required(configuration, TimeSeriesIntensiveHostConstants.CellId) != cell.Id
            || TimeSeriesIntensiveHostInput.Required(configuration, TimeSeriesIntensiveHostConstants.ContractSha256) != contract.ContractSha256)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
        var source = TimeSeriesIntensiveHostInput.Required(configuration, ComparisonHostConstants.SourceRevision);
        var identity = ComparisonExecutionIdentity.ReadTimeSeriesIntensive(configuration, source);
        var job = TimeSeriesIntensiveHostInput.ReadJob(configuration);
        var image = TimeSeriesIntensiveHostInput.Required(configuration, TimeSeriesIntensiveHostConstants.Image);
        if (!ComparisonExecutionIdentityImageReference.IsValid(image)
            || selection.Target == TimeSeriesIntensiveTargetKind.KeyLoad && image != identity.KeyLoadImage)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
        var output = TimeSeriesIntensiveHostInput.ReadOutput(configuration);
        var storage = TimeSeriesIntensiveHostInput.Required(configuration, ComparisonHostConstants.Storage);
        if (storage != TimeSeriesIntensiveHostConstants.StorageDescription)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
        var native = TimeSeriesIntensiveHostNativeSettings.Read(configuration, selection, image);
        return new(selection, cell, contract.ContractSha256, identity, job, image, output, storage,
            Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat, CultureInfo.InvariantCulture), native);
    }
}
