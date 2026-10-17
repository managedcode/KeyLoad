using System.Text.Json;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageSourceMetadata
{
    internal static void Verify(JsonElement metadata, string path, int maximumBytes)
    {
        NativeCoverageImageOracleSupport.RequireKeys(metadata, NativeCoverageImageFields.Length, NativeCoverageImageFields.Mode, NativeCoverageImageFields.Sha256);
        var info = new FileInfo(path);
        NativeCoverageImageOracleSupport.Ensure(info.Exists && info.Length <= maximumBytes
            && metadata.GetProperty(NativeCoverageImageFields.Length).GetInt64() == info.Length
            && metadata.GetProperty(NativeCoverageImageFields.Sha256).GetString() == NativeCoverageImageOracleSupport.HashFile(path, maximumBytes)
            && metadata.GetProperty(NativeCoverageImageFields.Mode).GetInt32() == NativeCoverageImageOracleSupport.Mode(path),
            "A producer source metadata entry differs from observed source bytes.");
    }
}
