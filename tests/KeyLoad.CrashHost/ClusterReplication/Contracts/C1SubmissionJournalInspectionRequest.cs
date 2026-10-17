using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed record C1SubmissionJournalInspectionRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string NodeDirectory,
    [property: JsonRequired] Guid ExpectedCanonicalNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] string LocalVoter,
    [property: JsonRequired] string[] Voters,
    [property: JsonRequired] long EntryIndex,
    [property: JsonRequired] long EntryTerm)
{
    public override string ToString() => nameof(C1SubmissionJournalInspectionRequest);
}
