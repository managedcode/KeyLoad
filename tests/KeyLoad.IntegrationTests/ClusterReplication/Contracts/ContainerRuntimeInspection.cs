namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains the original ordered Docker inspection fields.</summary>
/// <param name="Id">The full inspected container ID.</param>
/// <param name="ConfigImage">The configured image reference.</param>
/// <param name="ImageId">The actual image ID.</param>
/// <param name="State">The actual Docker runtime state.</param>
/// <param name="StartedAt">The actual Docker runtime start timestamp.</param>
internal sealed record ContainerRuntimeInspection(string Id, string ConfigImage, string ImageId, string State, string StartedAt);
