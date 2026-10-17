namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed record ControlledBlobLifetimeReadObservation(bool Failed, object? Value, string? Code);
