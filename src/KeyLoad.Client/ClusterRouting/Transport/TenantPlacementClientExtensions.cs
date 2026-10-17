using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Provides authenticated capacity-placement connection operations.</summary>
public static class TenantPlacementClientExtensions
{
    /// <summary>Executes ConfigurePlacementPolicyAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="commandId">The original commandId operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<TenantPlacementPolicyV1>> ConfigurePlacementPolicyAsync(this KeyLoadClient client,
        Guid commandId, ConfigurePlacementPolicyRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<TenantPlacementPolicyV1>(TenantPlacementPublicProtocol.ConfigureRoute, request, true, commandId, token);
    }

    /// <summary>Executes ReadPlacementPolicyAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<TenantPlacementPolicyV1>> ReadPlacementPolicyAsync(this KeyLoadClient client,
        TenantPlacementScope request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<TenantPlacementPolicyV1>(TenantPlacementPublicProtocol.PolicyRoute, request, false, Guid.Empty, token);
    }

    /// <summary>Executes PlanTenantPlacementAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<TenantPlacementPlanResultV1>> PlanTenantPlacementAsync(this KeyLoadClient client,
        TenantPlacementPlanRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<TenantPlacementPlanResultV1>(TenantPlacementPublicProtocol.PlanRoute, request, false, Guid.Empty, token);
    }

    /// <summary>Executes PersistPlacementIntentAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="commandId">The original commandId operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<PlacementIntentV1>> PersistPlacementIntentAsync(this KeyLoadClient client,
        Guid commandId, PersistPlacementIntentRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<PlacementIntentV1>(TenantPlacementPublicProtocol.PersistRoute, request, true, commandId, token);
    }

    /// <summary>Executes ApplyPlacementIntentAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="commandId">The original commandId operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<PlacementIntentV1>> ApplyPlacementIntentAsync(this KeyLoadClient client,
        Guid commandId, ApplyPlacementIntentRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<PlacementIntentV1>(TenantPlacementPublicProtocol.ApplyRoute, request, true, commandId, token);
    }

    /// <summary>Executes ReadPlacementIntentAsync through the authenticated connection.</summary>
    /// <param name="client">The original client operation value.</param>
    /// <param name="request">The original request operation value.</param>
    /// <param name="token">The original token operation value.</param>
    /// <returns>The original bounded native result.</returns>
    public static Task<Result<PlacementIntentV1>> ReadPlacementIntentAsync(this KeyLoadClient client,
        PlacementIntentReadRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Send<PlacementIntentV1>(TenantPlacementPublicProtocol.IntentRoute, request, false, Guid.Empty, token);
    }
}
