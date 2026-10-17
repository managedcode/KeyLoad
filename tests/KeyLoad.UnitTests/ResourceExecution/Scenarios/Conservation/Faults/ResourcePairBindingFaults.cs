using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingFaults
{
    internal static byte[] LifetimeKey(PartitionRef partition, string name)
        => KeyCodec.Encode(LifetimeSpace, partition.TenantId, partition.DatabaseId, name);

    internal static (byte[] Key, byte[] Original) Install(ResourcePairBindingState state, string name)
    {
        var partition = state.Source.Partition;
        var definition = KeySpace.Resource(partition.TenantId, partition.DatabaseId, name);
        var lifetime = LifetimeKey(partition, name);
        var key = state.Fault == ResourcePairBindingFault.MissingDefinition ? definition : lifetime;
        var original = ResourceIncarnationAssertions.Bytes(state.Source.Store, key);
        var changed = state.Fault switch
        {
            ResourcePairBindingFault.MissingLifetime or ResourcePairBindingFault.MissingDefinition => null,
            ResourcePairBindingFault.MalformedLifetime => [MalformedTag, MalformedLength, MalformedTail],
            ResourcePairBindingFault.ConflictingLifetime => Conflict(original),
            _ => throw new InvalidOperationException(Invalid)
        };
        state.Source.Store.Commit((transaction, _) =>
        {
            if (changed is null)
            {
                transaction.Delete(key);
            }
            else
            {
                transaction.Put(key, changed);
            }
            return true;
        });
        return (key, original);
    }

    internal static void Repair(ZoneTreeStore store, (byte[] Key, byte[] Original) row)
        => store.Commit((transaction, _) => { transaction.Put(row.Key, row.Original); return true; });

    private const byte MalformedTag = 1;
    private const byte MalformedLength = 2;
    private const byte MalformedTail = 3;
    private static byte[] Conflict(byte[] original)
    {
        var lifetime = NativeSerialization.Deserialize<ResourceLifetimeV1>(original);
        var value = lifetime.Incarnation.Value.ToCharArray();
        value[Zero] = value[Zero] == HexZero ? HexOne : HexZero;
        return NativeSerialization.Serialize(lifetime with
        {
            Incarnation = lifetime.Incarnation with
            {
                Value = new string(value)
            }
        });
    }
}
