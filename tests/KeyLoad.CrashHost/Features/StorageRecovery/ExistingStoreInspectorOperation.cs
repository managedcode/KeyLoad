using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class ExistingStoreInspectorOperation
{
    private const string FixtureKey = "guard/committed";
    private const string ValueTooLarge = "The original store value exceeds the inspection protocol.";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(FixtureKey);

    internal static ExistingStoreInspectionReceipt Run(ExistingStoreInspectionRequest request)
    {
        var failures = new List<Exception>();
        var inputs = new ExistingStoreInspectorInputs(request);
        ZoneTreeStore? store = null;
        Guid nodeId = default, incarnation = default;
        var format = 0;
        long position = 0;
        byte[]? value = null;
        try
        {
            ZoneTreeExistingStoreCleanup.Capture(() =>
            {
                store = ZoneTreeExistingStore.Open(inputs.CreateOptions()!, inputs.NodeId);
                nodeId = store.Identity.NodeId;
                incarnation = store.Identity.Incarnation;
                format = store.Identity.FormatVersion;
                position = store.Position;
                value = ReadValue(store);
                store.Dispose();
                store.Dispose();
            }, failures);
        }
        finally
        {
            ZoneTreeExistingStoreCleanup.Capture(() => store?.Dispose(), failures);
            ZoneTreeExistingStoreCleanup.Capture(() => inputs.Budget?.Dispose(), failures);
        }
        var (types, code) = ExistingStoreInspectorFailures.Collect(failures);
        return new(ExistingStoreInspectorProtocol.SchemaVersion, failures.Count == 0, nodeId, incarnation,
            format, position, value, types, code, inputs.ObservedStages, inputs.Budget?.GetSnapshot().RetainedBytes ?? 0);
    }

    private static byte[]? ReadValue(ZoneTreeStore store)
    {
        byte[]? value = null;
        store.Read(view => view.ReadValue(Key, borrowed =>
        {
            if (borrowed.Length > ExistingStoreInspectorProtocol.MaximumValueBytes)
            {
                throw new InvalidDataException(ValueTooLarge);
            }
            value = borrowed.ToArray();
        }));
        return value;
    }
}
