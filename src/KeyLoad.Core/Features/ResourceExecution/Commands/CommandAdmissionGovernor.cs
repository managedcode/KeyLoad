using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Reserves bounded node, tenant, principal, and retained-byte capacity for commands.</summary>
public sealed class CommandAdmissionGovernor
{
    private const long Utf16RetainedCharacterWidth = 2L;
    private const int AdjacentElementOffset = 1;

    private readonly Lock gate = new();
    private readonly Dictionary<(bool Control, string Id), int> tenants = [];
    private readonly Dictionary<(bool Control, string Id), int> principals = [];
    private int commands;
    private int controlCommands;
    private long retainedBytes;
    private long controlRetainedBytes;

    private const string ControlPayloadLimitDetail = "The control command exceeds its reserved byte budget.";
    private const string AdmissionLimitDetail = "The node, tenant or principal command admission budget is exhausted.";
    private const int EnvelopeOverheadBytes = 4_096;
    private const int MinimumPositiveCapacity = 1;
    private const string DerivedPoolHasNoCommandConfiguration = "An HTTP admission pool has HTTP configuration rather than command configuration.";
    private readonly CommandAdmissionLimits? configuredLimits;
    private readonly int maxCommands;
    private readonly long maxRetainedBytes;
    private readonly int maxTenantCommands;
    private readonly int maxPrincipalCommands;
    private readonly int reservedControlCommands;
    private readonly long reservedControlBytes;
    private readonly int maxControlPayloadBytes;
    private readonly int maxTenantControlCommands;
    private readonly int maxPrincipalControlCommands;

    /// <summary>Gets the immutable command admission limits used by this governor.</summary>
    public CommandAdmissionLimits Limits => configuredLimits ?? throw new InvalidOperationException(DerivedPoolHasNoCommandConfiguration);

    /// <summary>Creates a governor with validated limits.</summary>
    /// <param name="options">Centrally validated node-local limits, frozen for this admission owner.</param>
    public CommandAdmissionGovernor(IOptions<CommandAdmissionLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        configuredLimits = options.Value;
        configuredLimits.Validate();
        maxCommands = configuredLimits.MaxCommands;
        maxRetainedBytes = configuredLimits.MaxRetainedBytes;
        maxTenantCommands = configuredLimits.MaxTenantCommands;
        maxPrincipalCommands = configuredLimits.MaxPrincipalCommands;
        reservedControlCommands = configuredLimits.ReservedControlCommands;
        reservedControlBytes = configuredLimits.ReservedControlBytes;
        maxControlPayloadBytes = configuredLimits.MaxControlPayloadBytes;
        maxTenantControlCommands = configuredLimits.MaxTenantControlCommands;
        maxPrincipalControlCommands = configuredLimits.MaxPrincipalControlCommands;
    }

    private CommandAdmissionGovernor(int maxCommands, long maxRetainedBytes, int maxTenantCommands,
        int maxPrincipalCommands, int reservedControlCommands, long reservedControlBytes,
        int maxControlPayloadBytes, int maxTenantControlCommands, int maxPrincipalControlCommands)
    {
        if (maxCommands < MinimumPositiveCapacity || maxRetainedBytes < MinimumPositiveCapacity
            || maxTenantCommands < MinimumPositiveCapacity || maxPrincipalCommands < MinimumPositiveCapacity
            || reservedControlCommands < MinimumPositiveCapacity || reservedControlBytes < MinimumPositiveCapacity
            || maxControlPayloadBytes < MinimumPositiveCapacity || maxTenantControlCommands < MinimumPositiveCapacity
            || maxPrincipalControlCommands < MinimumPositiveCapacity)
        {
            throw new ArgumentException(CommandAdmissionLimits.ValidationMessage);
        }
        this.maxCommands = maxCommands;
        this.maxRetainedBytes = maxRetainedBytes;
        this.maxTenantCommands = maxTenantCommands;
        this.maxPrincipalCommands = maxPrincipalCommands;
        this.reservedControlCommands = reservedControlCommands;
        this.reservedControlBytes = reservedControlBytes;
        this.maxControlPayloadBytes = maxControlPayloadBytes;
        this.maxTenantControlCommands = maxTenantControlCommands;
        this.maxPrincipalControlCommands = maxPrincipalControlCommands;
    }

    internal static CommandAdmissionGovernor CreateDerivedHttpPool(int maxCommands, long maxRetainedBytes,
        int maxTenantCommands, int maxPrincipalCommands, int reservedControlCommands, long reservedControlBytes,
        int maxControlPayloadBytes, int maxTenantControlCommands, int maxPrincipalControlCommands)
        => new(maxCommands, maxRetainedBytes, maxTenantCommands, maxPrincipalCommands, reservedControlCommands,
            reservedControlBytes, maxControlPayloadBytes, maxTenantControlCommands, maxPrincipalControlCommands);

    /// <summary>Returns whether an operation uses the reserved control lane.</summary>
    /// <param name="kind">The operation kind to classify.</param>
    public static bool IsControl(OperationKind kind)
        => kind is OperationKind.Delivery or OperationKind.SubscriptionDelivery or OperationKind.Membership or OperationKind.SetDispatch
            or OperationKind.AbortBlobUpload or OperationKind.ReclaimBlob or OperationKind.BootstrapPhysicalShardCatalog
            or OperationKind.RegisterPhysicalOwner;

    /// <summary>Atomically reserves command count, retained bytes, tenant, and principal capacity.</summary>
    /// <param name="kind">The kind of operation being admitted.</param>
    /// <param name="principal">The verified principal responsible for the operation.</param>
    /// <param name="payloadBytes">The UTF-8 payload size.</param>
    /// <param name="payloadCharacters">The serialized payload character count.</param>
    /// <param name="cancellationToken">Cancels admission before a reservation is committed.</param>
    /// <returns>An owned reservation that releases capacity when disposed.</returns>
    public CommandAdmissionLease Reserve(OperationKind kind, PrincipalRecord principal, int payloadBytes,
        int payloadCharacters, CancellationToken cancellationToken = default)
        => Reserve(IsControl(kind), principal, payloadBytes, payloadCharacters, cancellationToken);

    internal CommandAdmissionLease Reserve(ReplicatedOperation operation, PrincipalRecord principal, int payloadBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var control = operation.Kind == OperationKind.RuntimeJournal
            ? RuntimeJournalBootstrapAdmission.IsControl(operation,
                Encoding.UTF8.GetByteCount(operation.PayloadJson), maxControlPayloadBytes)
            : IsControl(operation.Kind);
        return Reserve(control, principal, payloadBytes, operation.PayloadJson.Length, cancellationToken);
    }

    private CommandAdmissionLease Reserve(bool control, PrincipalRecord principal, int payloadBytes,
        int payloadCharacters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.TenantId);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadCharacters);

        var bytes = CalculateRetainedBytes(payloadBytes, payloadCharacters);
        if (control && payloadBytes > maxControlPayloadBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ControlPayloadLimitDetail);
        }

        var tenantKey = (control, principal.TenantId);
        var principalKey = (control, principal.Id);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapacity(control, bytes, tenantKey, principalKey);
            AddReservation(control, bytes, tenantKey, principalKey);
            return new(this, control, principal.TenantId, principal.Id, bytes);
        }
    }

    /// <summary>Returns a consistent snapshot of active reservations and scope entries.</summary>
    public CommandAdmissionSnapshot Snapshot()
    {
        lock (gate)
        {
            return new(commands, retainedBytes, controlCommands, controlRetainedBytes, tenants.Count, principals.Count);
        }
    }

    internal void Release(CommandAdmissionLease lease)
    {
        lock (gate)
        {
            RemoveReservation(lease);
            Decrement(tenants, (lease.Control, lease.Tenant));
            Decrement(principals, (lease.Control, lease.Principal));
        }
    }

    private static long CalculateRetainedBytes(int payloadBytes, int payloadCharacters)
        => checked(payloadCharacters * Utf16RetainedCharacterWidth + payloadBytes * Utf16RetainedCharacterWidth + EnvelopeOverheadBytes);

    private void EnsureCapacity(bool control, long bytes, (bool Control, string Id) tenantKey,
        (bool Control, string Id) principalKey)
    {
        var count = control ? controlCommands : commands;
        var used = control ? controlRetainedBytes : retainedBytes;
        var maxCount = control ? reservedControlCommands : maxCommands;
        var maxBytes = control ? reservedControlBytes : maxRetainedBytes;
        var maxTenant = control ? maxTenantControlCommands : maxTenantCommands;
        var maxPrincipal = control ? maxPrincipalControlCommands : maxPrincipalCommands;
        if (count >= maxCount || bytes > maxBytes - used || tenants.GetValueOrDefault(tenantKey) >= maxTenant
            || principals.GetValueOrDefault(principalKey) >= maxPrincipal)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AdmissionLimitDetail);
        }
    }

    private void AddReservation(bool control, long bytes, (bool Control, string Id) tenantKey,
        (bool Control, string Id) principalKey)
    {
        if (control)
        {
            controlCommands++;
            controlRetainedBytes += bytes;
        }
        else
        {
            commands++;
            retainedBytes += bytes;
        }
        tenants[tenantKey] = tenants.GetValueOrDefault(tenantKey) + AdjacentElementOffset;
        principals[principalKey] = principals.GetValueOrDefault(principalKey) + AdjacentElementOffset;
    }

    private void RemoveReservation(CommandAdmissionLease lease)
    {
        if (lease.Control)
        {
            controlCommands--;
            controlRetainedBytes -= lease.Bytes;
        }
        else
        {
            commands--;
            retainedBytes -= lease.Bytes;
        }
    }

    private static void Decrement(Dictionary<(bool Control, string Id), int> scopes, (bool Control, string Id) key)
    {
        if (scopes[key] == AdjacentElementOffset)
        {
            scopes.Remove(key);
        }
        else
        {
            scopes[key]--;
        }
    }
}
