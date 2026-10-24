namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueRenewedDeadlineRefusal(CommandRequest Request, ErrorCode Code, string Detail,
    string OriginalProblem);
