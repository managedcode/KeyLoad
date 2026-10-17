using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Validates the explicit fixture-owned original EventVector reservation barrier.</summary>
[ConfigurationBinding]
internal static class EventVectorReservedProbeProfile
{
    internal const string Setting = "KeyLoadTests:EventVectorReservedProbe";
    private const string Enabled = "true";
    private const string Registration = "KeyLoadTests:ClusterRouting:RegisterPhysicalOwners";
    private const string Invalid = "EventVectorReservedProbeProfileInvalid";
    private const int SectionPresence = 1;

    internal static bool Read(IConfiguration configuration, AppHostControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        var section = configuration.GetSection(Setting);
        var nested = section.GetChildren().Take(SectionPresence).Any();
        if (section.Value is null && !nested)
        {
            return false;
        }
        if (section.Value != Enabled || nested || !options.TwoRf3 || !options.Ephemeral
            || !options.RemoteDocumentReads || !options.RemotePartitionQueries
            || !options.ProtectedDocumentMovement || options.RequestProbe is null
            || options.NativeDiscoveryOmission is not null || options.Tests is not null
            || options.BenchmarksEnabled || options.ProtocolCohortEnabled
            || options.ProtocolCohortConfigured || options.TargetSelected
            || options.ComparisonSelectorsPresent
            || configuration[Registration] != Enabled)
        {
            throw new InvalidOperationException(Invalid);
        }
        return true;
    }
}
