namespace KeyLoad.Core;

public sealed class CommandAdmissionGovernor
{
    private readonly object gate = new();
    private readonly Dictionary<(bool Control, string Id), int> tenants = [];
    private readonly Dictionary<(bool Control, string Id), int> principals = [];
    private int commands, controlCommands;
    private long retainedBytes, controlRetainedBytes;
    public CommandAdmissionLimits Limits { get; }
    public CommandAdmissionGovernor(CommandAdmissionLimits? limits = null)
    {
        Limits = limits ?? new(); Limits.Validate();
    }
    public static bool IsControl(OperationKind kind)
        => kind is OperationKind.Delivery or OperationKind.SubscriptionDelivery or OperationKind.Membership or OperationKind.SetDispatch;
    public Lease Reserve(OperationKind kind, PrincipalRecord principal, int payloadBytes, int payloadCharacters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payloadBytes < 0 || payloadCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        // JSON embedded in the Raft envelope escapes quotes/backslashes again; reserve two UTF-8 copies.
        var bytes = checked(payloadCharacters * 2L + payloadBytes * 2L + 4_096);
        var control = IsControl(kind);
        if (control && payloadBytes > Limits.MaxControlPayloadBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The control command exceeds its reserved byte budget.");
        var tenantKey = (control, principal.TenantId); var principalKey = (control, principal.Id);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = control ? controlCommands : commands;
            var used = control ? controlRetainedBytes : retainedBytes;
            var maxCount = control ? Limits.ReservedControlCommands : Limits.MaxCommands;
            var maxBytes = control ? Limits.ReservedControlBytes : Limits.MaxRetainedBytes;
            var maxTenant = control ? Limits.MaxTenantControlCommands : Limits.MaxTenantCommands;
            var maxPrincipal = control ? Limits.MaxPrincipalControlCommands : Limits.MaxPrincipalCommands;
            var tenantCount = tenants.GetValueOrDefault(tenantKey); var principalCount = principals.GetValueOrDefault(principalKey);
            if (count >= maxCount || bytes > maxBytes - used || tenantCount >= maxTenant || principalCount >= maxPrincipal)
                throw Errors.Fail(ErrorCode.ResourceExhausted, "The node, tenant or principal command admission budget is exhausted.");
            if (control) { controlCommands++; controlRetainedBytes += bytes; }
            else { commands++; retainedBytes += bytes; }
            tenants[tenantKey] = tenantCount + 1; principals[principalKey] = principalCount + 1;
            return new(this, control, principal.TenantId, principal.Id, bytes);
        }
    }
    public CommandAdmissionSnapshot Snapshot()
    {
        lock (gate) return new(commands, retainedBytes, controlCommands, controlRetainedBytes, tenants.Count, principals.Count);
    }
    private void Release(Lease lease)
    {
        lock (gate)
        {
            if (lease.Released) return;
            lease.Released = true;
            if (lease.Control) { controlCommands--; controlRetainedBytes -= lease.Bytes; }
            else { commands--; retainedBytes -= lease.Bytes; }
            Decrement(tenants, (lease.Control, lease.Tenant)); Decrement(principals, (lease.Control, lease.Principal));
        }
    }
    private static void Decrement(Dictionary<(bool Control, string Id), int> scopes, (bool Control, string Id) key)
    {
        if (scopes[key] == 1) scopes.Remove(key); else scopes[key]--;
    }
    public sealed class Lease : IDisposable
    {
        private readonly CommandAdmissionGovernor owner;
        internal bool Control { get; }
        internal string Tenant { get; }
        internal string Principal { get; }
        internal long Bytes { get; }
        internal bool Released { get; set; }
        internal Lease(CommandAdmissionGovernor owner, bool control, string tenant, string principal, long bytes)
        { this.owner = owner; Control = control; Tenant = tenant; Principal = principal; Bytes = bytes; }
        public void Dispose() => owner.Release(this);
    }
}
