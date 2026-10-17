using System.Globalization;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireNames
{
    internal const string TemporarySuffix = ".tmp";
    private const string ResultSeparator = "-";
    internal static string Claim(NativeBlobWireIdentity identity)
        => NativeBlobWireProbeProtocol.ClaimPrefix + Index(identity) + NativeBlobWireProbeProtocol.JsonSuffix;
    internal static string Completion(NativeBlobWireIdentity identity)
        => NativeBlobWireProbeProtocol.CompletePrefix + Index(identity) + NativeBlobWireProbeProtocol.JsonSuffix;
    internal static string Result(NativeBlobWireIdentity identity, int ordinal)
        => NativeBlobWireProbeProtocol.ResultPrefix + Index(identity) + ResultSeparator
            + ordinal.ToString(NativeBlobWireProbeProtocol.OrdinalFormat, CultureInfo.InvariantCulture)
            + NativeBlobWireProbeProtocol.JsonSuffix;
    internal static string Slot(int slot)
        => NativeBlobWireProbeProtocol.SlotPrefix
            + slot.ToString(NativeBlobWireProbeProtocol.SlotFormat, CultureInfo.InvariantCulture)
            + NativeBlobWireProbeProtocol.JsonSuffix;
    private static string Index(NativeBlobWireIdentity identity)
        => ((int)identity).ToString(NativeBlobWireProbeProtocol.OrdinalFormat, CultureInfo.InvariantCulture);
}
