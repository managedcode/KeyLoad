using Microsoft.Extensions.Configuration;
using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3Profile
{
    internal static bool ValidateAndRead(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(TwoRf3ProfileProtocol.Section);
        var children = section.GetChildren().ToArray();
        if (children.Any(child => child.Key != "Profile") || children.Length > 1)
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
