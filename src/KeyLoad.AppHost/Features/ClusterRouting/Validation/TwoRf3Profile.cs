using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

[ConfigurationBinding]
internal static class TwoRf3Profile
{
    private const string ProfileSetting = "Profile";

    internal static bool ValidateAndRead(IConfiguration configuration)
    {
        const int BoundaryValue = 1;

        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(TwoRf3ProfileProtocol.Section);
        var children = section.GetChildren().ToArray();
        if (children.Any(child => child.Key != ProfileSetting) || children.Length > BoundaryValue)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var selected = configuration[TwoRf3ProfileProtocol.Setting];
        if (selected is null)
        { return false; }
        if (selected != TwoRf3ProfileProtocol.Profile || !configuration.GetValue<bool>(TwoRf3ProfileProtocol.EphemeralSetting)
            || !string.IsNullOrWhiteSpace(configuration[TestSuiteSettings.SuiteSetting])
            || configuration.GetValue<bool>(TwoRf3ProfileProtocol.BenchmarksEnabledSetting)
            || configuration[Comparisons.ComparisonWorkerSelection.TargetSetting] is not null)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        return true;
    }
}
