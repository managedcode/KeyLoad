using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[JsonSerializable(typeof(C1SubmissionOutcomeInspectionRequest))]
[JsonSerializable(typeof(C1SubmissionOutcomeInspectionReceipt))]
internal sealed partial class C1SubmissionOutcomeInspectionJsonContext : JsonSerializerContext;
