namespace KeyLoad.Core;
/// <summary>Executes MessagingSerialization operations against the caller's database context.</summary>
public static class MessagingSerialization
{
    internal static byte[] GroupKey(string space, SubscriptionRef subscription, params ReadOnlySpan<KeyLoad.Storage.KeyComponent> suffix)
    {
        var source = subscription.Source;
        return KeySpace.Partition(space, source.Partition, [source.Resource, source.Kind.ToString(), source.StreamId, source.Generation, subscription.GroupId, .. suffix]);
    }
}
