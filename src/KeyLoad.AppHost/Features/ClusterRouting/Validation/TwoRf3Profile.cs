using System.Globalization;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.AppHost.Features.ClusterRouting;

[ConfigurationBinding]
internal static class TwoRf3Profile
{
    private const string ProfileSetting = "Profile";
    private const string ProtectedDocumentMovementKey = "ProtectedDocumentMovement";
    private const string RemoteQueryKey = "RemotePartitionQueries";
    private const string RemoteDocumentKey = "RemoteDocumentReads";
    private const string MovementMaxBatchBytesKey = "MovementMaxBatchBytes";
    private const string MovementMaxFrameBytesKey = "MovementMaxFrameBytes";
    private const string RegistrationKey = "RegisterPhysicalOwners";

    internal static bool ReadRegistration(IConfiguration configuration)
    {
        _ = ValidateAndRead(configuration);
        return configuration[TwoRf3ProfileProtocol.RegistrationSetting] == TwoRf3ProfileProtocol.Enabled;
    }

    internal static int? ReadMovementMaxBatchBytes(IConfiguration configuration)
    {
        var configured = configuration[TwoRf3ProfileProtocol.MovementMaxBatchBytesSetting];
        if (configured is null)
        { return null; }
        if (configuration[TwoRf3ProfileProtocol.Setting] != TwoRf3ProfileProtocol.Profile
            || configuration[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting] != TwoRf3ProfileProtocol.Enabled
            || !int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        new DatabaseLimits { MaxBatchBytes = bytes }.Validate();
        return bytes;
    }

    internal static int? ReadMovementMaxFrameBytes(IConfiguration configuration)
    {
        var configured = configuration[TwoRf3ProfileProtocol.MovementMaxFrameBytesSetting];
        if (configured is null)
        { return null; }
        if (configuration[TwoRf3ProfileProtocol.Setting] != TwoRf3ProfileProtocol.Profile
            || configuration[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting] != TwoRf3ProfileProtocol.Enabled
            || !int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        new ZoneTreeStorageExecutionOptions { MaxFrameBytes = bytes }.Validate();
        return bytes;
    }

    internal static bool ValidateAndRead(IConfiguration configuration)
    {
        const int MaximumSettings = 7;

        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(TwoRf3ProfileProtocol.Section);
        var children = section.GetChildren().ToArray();
        if (children.Any(child => (child.Key is not ProfileSetting and not RegistrationKey and not RemoteDocumentKey and not RemoteQueryKey and not ProtectedDocumentMovementKey and not MovementMaxBatchBytesKey and not MovementMaxFrameBytesKey) || child.GetChildren().Any()) || children.Length > MaximumSettings)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var registration = configuration[TwoRf3ProfileProtocol.RegistrationSetting];
        if (registration is not null and not TwoRf3ProfileProtocol.Enabled and not TwoRf3ProfileProtocol.Disabled)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var remote = configuration[TwoRf3ProfileProtocol.RemoteDocumentSetting];
        if (remote is not null and not TwoRf3ProfileProtocol.Enabled and not TwoRf3ProfileProtocol.Disabled
            || remote == TwoRf3ProfileProtocol.Enabled && registration != TwoRf3ProfileProtocol.Enabled)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var query = configuration[TwoRf3ProfileProtocol.RemoteQuerySetting];
        if (query is not null and not TwoRf3ProfileProtocol.Enabled and not TwoRf3ProfileProtocol.Disabled
            || query == TwoRf3ProfileProtocol.Enabled && remote != TwoRf3ProfileProtocol.Enabled)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var movement = configuration[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting];
        if (movement is not null and not TwoRf3ProfileProtocol.Enabled and not TwoRf3ProfileProtocol.Disabled
            || movement == TwoRf3ProfileProtocol.Enabled && query != TwoRf3ProfileProtocol.Enabled)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        var maxBatchBytes = configuration[TwoRf3ProfileProtocol.MovementMaxBatchBytesSetting];
        if (maxBatchBytes is not null)
        { _ = ReadMovementMaxBatchBytes(configuration); }
        var maxFrameBytes = configuration[TwoRf3ProfileProtocol.MovementMaxFrameBytesSetting];
        if (maxFrameBytes is not null)
        { _ = ReadMovementMaxFrameBytes(configuration); }
        var selected = configuration[TwoRf3ProfileProtocol.Setting];
        if (selected is null)
        {
            if (registration is not null || remote is not null || query is not null || movement is not null || maxBatchBytes is not null || maxFrameBytes is not null)
            { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
            return false;
        }
        if (selected != TwoRf3ProfileProtocol.Profile || !configuration.GetValue<bool>(TwoRf3ProfileProtocol.EphemeralSetting)
            || !string.IsNullOrWhiteSpace(configuration[TestSuiteSettings.SuiteSetting])
            || configuration.GetValue<bool>(TwoRf3ProfileProtocol.BenchmarksEnabledSetting)
            || configuration[Comparisons.ComparisonWorkerSelection.TargetSetting] is not null)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        return true;
    }
}
