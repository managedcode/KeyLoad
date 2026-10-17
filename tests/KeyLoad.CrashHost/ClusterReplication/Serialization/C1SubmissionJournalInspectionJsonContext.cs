using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

[JsonSerializable(typeof(C1SubmissionJournalInspectionRequest))]
[JsonSerializable(typeof(C1SubmissionJournalInspectionReceipt))]
internal sealed partial class C1SubmissionJournalInspectionJsonContext : JsonSerializerContext;
