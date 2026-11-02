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
    private readonly record struct OwnerLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Gate, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Budget, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Charge, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Head, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ ReleaseFailure, bool Closed, bool Released);
    private readonly record struct LeaseLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Owner, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Entry, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Charge, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ ReleaseFailure, bool ChargeReleased, bool PinReleased);
    private readonly record struct EntryLayout(RowVisibilityScope Scope, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Budget, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Charge,
        object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Ids, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Index, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Next, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ ReleaseFailure, int Pins, bool Retired, bool Disposed);
    private readonly record struct IdLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Id, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Charge, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Next);
    private readonly record struct DraftLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Owner, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Entry);
    private readonly record struct ReservationLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Owner, long Bytes, int Entries, int Released);
    private readonly record struct SetLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Root, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Comparer, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ SerializationInfo, int Count, int Version);
    private readonly record struct SetNodeLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Item, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Left, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Right, int Color);
    private readonly record struct PartitionLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Tenant, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Database, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Domain, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ Key);
    private readonly record struct LockLayout(ulong ThreadId, uint State, uint RecursionCount, short SpinCount,
        ushort WaiterStartTime, object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ WaitEvent);
    private readonly record struct WaitEventLayout(object /* KLD0041: Only Unsafe.SizeOf reads this uninstantiated layout; this field models one CLR reference slot. */ SafeHandle);
    private readonly record struct SafeHandleLayout(IntPtr Handle, int State, bool OwnsHandle, bool FullyInitialized);
}
