namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static void RequireNativeTextProjectionHistory(OutboxHead head, long checkpoint)
        => CheckOutboxPosition(head, checkpoint);
}
