using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionDeliveryAuthorityFaults
{
    private const long ForeignPosition = 99;
    private const int UndefinedState = 99;

    internal static void Stage(IAtomicTransaction tx, byte[] key, GroupDelivery row,
        SubscriptionDeliveryAuthorityFault fault)
    {
        if (fault == SubscriptionDeliveryAuthorityFault.MissingWindow)
        {
            tx.Delete(key);
            return;
        }
        tx.PutRecord(key, Inject(row, fault));
    }

    internal static GroupDelivery Inject(GroupDelivery row, SubscriptionDeliveryAuthorityFault fault)
        => fault switch
        {
            SubscriptionDeliveryAuthorityFault.ForeignPosition => row with { Position = ForeignPosition },
            SubscriptionDeliveryAuthorityFault.MissingLeaseExpiry => row with { LeaseUntil = null },
            SubscriptionDeliveryAuthorityFault.UndefinedState => row with { State = (GroupDeliveryState)UndefinedState },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
}
