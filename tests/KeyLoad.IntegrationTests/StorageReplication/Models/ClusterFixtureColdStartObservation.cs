namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Actual selected fixture storage path existence immediately before the execution AppHost builder is created.</summary>
internal sealed record ClusterFixtureColdStartObservation(string Root, bool DirectoryExisted, bool FileExisted);
