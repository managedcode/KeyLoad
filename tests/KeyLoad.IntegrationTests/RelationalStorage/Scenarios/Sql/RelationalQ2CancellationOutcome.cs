namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record RelationalQ2CancellationOutcome(bool ReturnedSuccess, string? ErrorCode,
    OperationCanceledException? Cancellation);
