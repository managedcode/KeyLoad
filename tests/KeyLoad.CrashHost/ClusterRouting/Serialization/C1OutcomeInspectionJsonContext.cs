using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(NativeInstallFrameInspectionRequest))]
[JsonSerializable(typeof(NativeInstallFrameInspectionReceipt))]
[JsonSerializable(typeof(C1OutcomeInspectionRequest))]
[JsonSerializable(typeof(C1OutcomeInspectionReceipt))]
internal sealed partial class C1OutcomeInspectionJsonContext : JsonSerializerContext
{
}
