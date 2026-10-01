namespace KeyLoad;

public sealed record HttpAdmissionLimits
{
    public int MaxRequests { get; init; } = 32;
    public long MaxReservedBytes { get; init; } = 1_073_741_824;
    public int MaxTenantRequests { get; init; } = 16;
    public int MaxPrincipalRequests { get; init; } = 8;
    public int ReservedControlRequests { get; init; } = 16;
    public long ReservedControlBytes { get; init; } = 8_388_608;
    public int MaxTenantControlRequests { get; init; } = 8;
    public int MaxPrincipalControlRequests { get; init; } = 8;
    public int MaxBodyBytes { get; init; } = 8_388_608;
    public int MaxControlBodyBytes { get; init; } = 65_536;
    public long HeavyReadReservedBytes { get; init; } = 67_108_864;
    public long OtherReservedBytes { get; init; } = 8_388_608;
    public void Validate()
    {
        if (MaxRequests < 1 || MaxReservedBytes < 1 || MaxTenantRequests < 1 || MaxPrincipalRequests < 1
            || ReservedControlRequests < 1 || ReservedControlBytes < 1 || MaxTenantControlRequests < 1 || MaxPrincipalControlRequests < 1
            || MaxBodyBytes is < 1 or > 33_554_432 || MaxControlBodyBytes < 1 || MaxControlBodyBytes > MaxBodyBytes
            || HeavyReadReservedBytes is < 0 or > 1_073_741_824 || OtherReservedBytes is < 0 or > 1_073_741_824)
            throw new ArgumentException("HTTP admission limits are invalid.");
    }
}
public sealed record HttpAdmissionStatus(HttpAdmissionLimits Limits, CommandAdmissionSnapshot Node, CommandAdmissionSnapshot VerifiedScopes);
