namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Actual selected fixture storage path existence immediately before the native AppHost builder is created.</summary>
internal sealed record ClusterFixtureColdStartObservation(string Root, bool DirectoryExisted, bool FileExisted);
