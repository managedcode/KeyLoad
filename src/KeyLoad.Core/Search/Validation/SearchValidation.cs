namespace KeyLoad.Core;
/// <summary>Executes SearchValidation operations against the caller's database context.</summary>
public static class SearchValidation
{
    internal static void RequireTextProjectionHistory(OutboxHead head, long checkpoint) => ProjectionOutbox.CheckOutboxPosition(head, checkpoint);
}
