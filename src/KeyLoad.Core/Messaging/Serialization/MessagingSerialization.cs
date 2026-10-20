namespace KeyLoad.Core;
/// <summary>Executes MessagingSerialization operations against the caller's database context.</summary>
public static class MessagingSerialization
{
    internal static byte[] GroupKey(string space, SubscriptionRef subscription, params object?[] suffix)
    {
        var source = subscription.Source;
        return KeySpace.Partition(space, source.Partition, new object?[] { source.Resource, source.Kind.ToString(), source.StreamId, source.Generation, subscription.GroupId }.Concat(suffix).ToArray());
    }
}
