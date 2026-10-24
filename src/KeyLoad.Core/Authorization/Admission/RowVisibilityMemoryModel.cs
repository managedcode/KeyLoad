using System.Runtime.CompilerServices;

namespace KeyLoad.Core.Authorization;

internal static class RowVisibilityMemoryModel
{
    private const int ObjectHeaderPointers = 2;
    internal const int SingleEntry = 1;
    internal const int MetadataOnly = 0;

    internal static long OwnerBytes => ObjectBytes<OwnerLayout>() + ObjectBytes<LockLayout>()
        + ObjectBytes<WaitEventLayout>() + ObjectBytes<SafeHandleLayout>() + ReservationBytes;
    internal static long LeaseBytes => ObjectBytes<LeaseLayout>() + ReservationBytes;
    internal static long EntryBytes(RowVisibilityScope scope)
        => ObjectBytes<EntryLayout>() + ReservationBytes + ObjectBytes<DraftLayout>()
            + ObjectBytes<SetLayout>() + ObjectBytes<PartitionLayout>()
            + StringBytes(scope.Partition.TenantId) + StringBytes(scope.Partition.DatabaseId)
            + StringBytes(scope.Partition.TransactionDomainId) + StringBytes(scope.Partition.PartitionKey)
            + StringBytes(scope.Collection) + StringBytes(scope.PrincipalId) + StringBytes(scope.PrincipalTenant);
    internal static long IdBytes(string id)
        => ObjectBytes<IdLayout>() + StringBytes(id) + ReservationBytes + ObjectBytes<SetNodeLayout>();

    private static long ReservationBytes => ObjectBytes<ReservationLayout>();
    private static long ObjectBytes<T>() where T : struct
        => Align(ObjectHeaderPointers * IntPtr.Size + Unsafe.SizeOf<T>());
    private static long Align(long bytes)
        => (bytes + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;
    private static long StringBytes(string value)
        => Align(ObjectHeaderPointers * IntPtr.Size + 1 * sizeof(int)
            + ((long)value.Length + 1) * sizeof(char));
    private readonly record struct OwnerLayout(object Gate, object Budget, object Charge, object Head, object ReleaseFailure, bool Closed, bool Released);
    private readonly record struct LeaseLayout(object Owner, object Entry, object Charge, object ReleaseFailure, bool ChargeReleased, bool PinReleased);
    private readonly record struct EntryLayout(RowVisibilityScope Scope, object Budget, object Charge,
        object Ids, object Index, object Next, object ReleaseFailure, int Pins, bool Retired, bool Disposed);
    private readonly record struct IdLayout(object Id, object Charge, object Next);
    private readonly record struct DraftLayout(object Owner, object Entry);
    private readonly record struct ReservationLayout(object Owner, long Bytes, int Entries, int Released);
    private readonly record struct SetLayout(object Root, object Comparer, object SerializationInfo, int Count, int Version);
    private readonly record struct SetNodeLayout(object Item, object Left, object Right, int Color);
    private readonly record struct PartitionLayout(object Tenant, object Database, object Domain, object Key);
    private readonly record struct LockLayout(ulong ThreadId, uint State, uint RecursionCount, short SpinCount,
        ushort WaiterStartTime, object WaitEvent);
    private readonly record struct WaitEventLayout(object SafeHandle);
    private readonly record struct SafeHandleLayout(IntPtr Handle, int State, bool OwnsHandle, bool FullyInitialized);
}
