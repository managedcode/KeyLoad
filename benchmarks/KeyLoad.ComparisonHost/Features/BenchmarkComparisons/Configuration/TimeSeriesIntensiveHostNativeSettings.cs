using System.Collections.Immutable;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal sealed record TimeSeriesIntensiveHostNativeSettings(string Image, ImmutableArray<Uri> Endpoints,
    ImmutableArray<string> VoterIds, Guid? Incarnation, string? AdminKey, string? ConnectionString)
{
    public override string ToString() => nameof(TimeSeriesIntensiveHostNativeSettings);

    internal static TimeSeriesIntensiveHostNativeSettings Read(IConfiguration configuration,
        TimeSeriesIntensiveSelection selection, string image)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            selection.Validate();
            TimeSeriesIntensiveHostInput.ValidateNativeSection(configuration, selection.Target);
            if (!ComparisonExecutionIdentityImageReference.IsValid(image)
                || TimeSeriesIntensiveHostInput.Required(configuration, TimeSeriesIntensiveHostConstants.Image) != image)
            {
                throw TimeSeriesIntensiveHostInput.Invalid();
            }
            return selection.Target == TimeSeriesIntensiveTargetKind.KeyLoad
                ? ReadKeyLoad(configuration, selection.NodeCount, image)
                : ReadTimescale(configuration, selection.NodeCount, image);
        }
        catch (InvalidOperationException)
        {
            throw TimeSeriesIntensiveHostInput.Invalid();
        }
    }

    private static TimeSeriesIntensiveHostNativeSettings ReadKeyLoad(IConfiguration configuration, int count, string image)
    {
        TimeSeriesIntensiveHostInput.RejectPresent(configuration, TimeSeriesIntensiveHostConstants.ConnectionString);
        var endpoints = TimeSeriesIntensiveHostInput.ReadEndpoints(configuration, TimeSeriesIntensiveHostConstants.Endpoints, count, tcp: false);
        var voters = TimeSeriesIntensiveHostInput.ReadArray(configuration, TimeSeriesIntensiveHostConstants.VoterIds, count);
        _ = TimeSeriesIntensiveHostInput.ValidateAuthorities(voters, tcp: false);
        var incarnation = TimeSeriesIntensiveHostInput.ReadIncarnation(configuration);
        var admin = TimeSeriesIntensiveHostInput.Required(configuration, ComparisonHostConstants.AdminKey);
        return new(image, endpoints, voters, incarnation, admin, null);
    }

    private static TimeSeriesIntensiveHostNativeSettings ReadTimescale(IConfiguration configuration, int count, string image)
    {
        TimeSeriesIntensiveHostInput.RejectPresent(configuration, ComparisonHostConstants.AdminKey);
        TimeSeriesIntensiveHostInput.RejectPresent(configuration, TimeSeriesIntensiveHostConstants.Incarnation);
        TimeSeriesIntensiveHostInput.RejectPresent(configuration, TimeSeriesIntensiveHostConstants.VoterIds);
        var endpoints = TimeSeriesIntensiveHostInput.ReadEndpoints(configuration, TimeSeriesIntensiveHostConstants.Endpoints, count, tcp: true);
        var connection = TimeSeriesIntensiveHostInput.ReadConnection(configuration);
        return new(image, endpoints, ImmutableArray<string>.Empty, null, null, connection);
    }
}
