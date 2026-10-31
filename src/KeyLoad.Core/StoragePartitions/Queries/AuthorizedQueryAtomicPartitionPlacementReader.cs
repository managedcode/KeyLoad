using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizedQueryAtomicPartitionPlacementReader operations against the caller's database context.</summary>
public static class AuthorizedQueryAtomicPartitionPlacementReader
{
    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedQuery(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition)
    {
        return engine.ReadPlacementWitness(view, partition);
    }

    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacementForAuthorizedRouting(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition) => engine.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition);
}
