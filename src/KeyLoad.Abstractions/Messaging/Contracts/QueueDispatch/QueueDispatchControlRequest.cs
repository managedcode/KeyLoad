namespace KeyLoad;

/// <summary>Changes one existing queue pause state with an explicit current schema version.</summary>
/// <param name="TenantId">The canonical tenant containing the queue.</param>
/// <param name="DatabaseId">The canonical database containing the queue.</param>
/// <param name="Queue">The existing work queue name.</param>
/// <param name="ExpectedSchemaVersion">The current persisted resource version required by the CAS.</param>
/// <param name="Paused">The desired pause state; all other metadata is server-owned.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(QueueDispatchControlProtocol.RequestAlias)]
public sealed record QueueDispatchControlRequest(
    [property: Orleans.Id(0), System.Text.Json.Serialization.JsonRequired] string TenantId,
    [property: Orleans.Id(1), System.Text.Json.Serialization.JsonRequired] string DatabaseId,
    [property: Orleans.Id(2), System.Text.Json.Serialization.JsonRequired] string Queue,
    [property: Orleans.Id(3), System.Text.Json.Serialization.JsonRequired] long ExpectedSchemaVersion,
    [property: Orleans.Id(4), System.Text.Json.Serialization.JsonRequired] bool Paused);
