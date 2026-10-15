using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Admits explicit local protected movement observations without enrolling other diagnostic profiles.</summary>
[ConfigurationBinding]
internal static class ProtectedMovementProbeProfile
{
    internal const string Setting = "KeyLoadTests:ProtectedMovementProbe";
    internal const string Invalid = "ProtectedMovementProbeProfileInvalid";
    private const string Enabled = "true";
    private const string Registration = "KeyLoadTests:ClusterRouting:RegisterPhysicalOwners";
    private const int SectionPresence = 1;

    internal static bool Read(IConfiguration configuration, AppHostControlOptions options)
    {
        var section = configuration.GetSection(Setting);
        var nested = section.GetChildren().Take(SectionPresence).Any();
        if (section.Value is null && !nested)
        {
            return false;
        }
        if (section.Value != Enabled || nested || !options.TwoRf3 || !options.Ephemeral
            || !options.RemoteDocumentReads || !options.RemotePartitionQueries
            || !options.ProtectedDocumentMovement || options.RequestProbe is null
            || options.NativeDiscoveryOmission is not null || options.EventVectorReservedProbe
            || options.NativeBlobWireProbe is not null || options.MovementFrameObservation is not null
            || options.Tests is not null || options.BenchmarksEnabled || options.ProtocolCohortEnabled
            || options.ProtocolCohortConfigured || options.TargetSelected
            || options.ComparisonSelectorsPresent || options.ScaleSelected
            || configuration[Registration] != Enabled)
        {
            throw new InvalidOperationException(Invalid);
        }
        return true;
    }
}
