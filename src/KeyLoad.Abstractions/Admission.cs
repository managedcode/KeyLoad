namespace KeyLoad;

// Node-local admission settings may differ between voters; they never change canonical apply decisions.
public sealed record CommandAdmissionLimits
{
    public int MaxCommands { get; init; } = 256;
    public long MaxRetainedBytes { get; init; } = 134_217_728;
    public int MaxTenantCommands { get; init; } = 128;
    public int MaxPrincipalCommands { get; init; } = 64;
    public int ReservedControlCommands { get; init; } = 16;
    public long ReservedControlBytes { get; init; } = 2_097_152;
    public int MaxControlPayloadBytes { get; init; } = 65_536;
    public int MaxTenantControlCommands { get; init; } = 8;
    public int MaxPrincipalControlCommands { get; init; } = 8;
    public void Validate()
    {
        if (MaxCommands < 1 || MaxRetainedBytes < 1 || MaxTenantCommands < 1 || MaxPrincipalCommands < 1
            || ReservedControlCommands < 1 || ReservedControlBytes < 1 || MaxControlPayloadBytes < 1
            || MaxTenantControlCommands < 1 || MaxPrincipalControlCommands < 1)
            throw new ArgumentException("Command admission limits must be positive.");
    }
}
public sealed record CommandAdmissionSnapshot(int Commands, long RetainedBytes, int ControlCommands, long ControlRetainedBytes,
    int ActiveTenantScopes, int ActivePrincipalScopes);
public sealed record NodeAdmissionStatus(CommandAdmissionLimits Limits, CommandAdmissionSnapshot Usage);
