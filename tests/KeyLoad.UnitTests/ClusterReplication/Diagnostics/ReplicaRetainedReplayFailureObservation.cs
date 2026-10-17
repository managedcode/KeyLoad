using System.Text.Json;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Observes only local original replica metadata after a failed single control barrier, before teardown.</summary>
internal static class ReplicaRetainedReplayFailureObservation
{
    private const string Kind = "NativeRetainedReplayBarrierFailure";
    private const string StageKey = "KeyLoad.Replication.ReadBarrier.Stage";
    private const string FlagsKey = "KeyLoad.Replication.ReadBarrier.Flags";
    private const string Invalid = "The original retained replay failure observation is invalid.";
    private const int Version = 1;
    private const int MaximumVoters = 3;
    private const int MaximumCharacters = 1024;
    private const int FirstStage = ReplicaReadFailureMetadata.Transport;
    private const int LastStage = ReplicaReadFailureMetadata.LeaderTerm;
    private const int NoFlags = 0;
    private const int AllFlags = 7;

    internal static async Task ReportAsync(RequestCqrsCohortRuntimeFixture fixture, string failingVoter,
        Exception primary, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var voters = ReplicaBarrierDeadlineFlow.Voters;
            if (voters.Length != MaximumVoters || !voters.Contains(failingVoter, StringComparer.Ordinal))
            {
                throw new InvalidDataException(Invalid);
            }
            var stage = Metadata(primary, StageKey, FirstStage, LastStage);
            var flags = Metadata(primary, FlagsKey, NoFlags, AllFlags);
            foreach (var voter in voters)
            {
                await ServerFailureObserver.ObserveAsync(() => RecordAsync(fixture.Node(voter), failingVoter,
                    stage, flags, failures, token), failures).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
    }

    private static async Task RecordAsync(RequestCqrsCohortNativeNode node, string failingVoter,
        int? stage, int? flags, List<Exception> failures, CancellationToken token)
    {
        ReplicaNodeState? state = null;
        int? generation = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var observed = await node.Journal.Consensus.StateAsync(token).ConfigureAwait(false);
            if (observed.VoterId != node.Journal.Configuration.LocalId || !Enum.IsDefined(observed.Role)
                || observed.LeaderId is { } leader && !ReplicaBarrierDeadlineFlow.Voters.Contains(leader, StringComparer.Ordinal))
            {
                throw new InvalidDataException(Invalid);
            }
            generation = Orleans.Runtime.SiloAddress.FromParsableString(node.Discovery.RuntimeAddress).Generation;
            state = observed;
        }, failures).ConfigureAwait(false);
        var text = JsonSerializer.Serialize(new
        {
            Kind,
            Version,
            FailingVoter = failingVoter,
            Stage = stage,
            Flags = flags,
            VoterId = node.Journal.Configuration.LocalId,
            LocalStateObserved = state is not null,
            LeaderId = state?.LeaderId,
            Role = state?.Role.ToString(),
            Term = state?.Term,
            LastIndex = state?.LastIndex,
            CommittedIndex = state?.CommittedIndex,
            AppliedIndex = state?.MaterializedPosition,
            NativeGeneration = generation
        });
        if (text.Length > MaximumCharacters)
        {
            throw new InvalidDataException(Invalid);
        }
        await Console.Error.WriteLineAsync(text).ConfigureAwait(false);
    }

    private static int? Metadata(Exception primary, string key, int first, int last)
    {
        if (primary.Data[key] is null)
        {
            return null;
        }
        if (primary.Data[key] is not int value || value < first || value > last)
        {
            throw new InvalidDataException(Invalid);
        }
        return value;
    }
}
