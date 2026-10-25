namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueRenewedDeadlineRefusal(CommandRequest Request, ErrorCode Code, string Detail,
    string OriginalProblem);
