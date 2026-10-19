namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static void RequireTextProjectionHistory(OutboxHead head, long checkpoint)
        => CheckOutboxPosition(head, checkpoint);
}
