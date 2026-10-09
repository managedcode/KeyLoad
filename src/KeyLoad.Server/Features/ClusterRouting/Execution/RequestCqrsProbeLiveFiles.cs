using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Reads/writes diagnostic controls under the original owner lock, identity ledger and quotas.</summary>
internal sealed class RequestCqrsProbeLiveFiles(string root, string session, RequestCqrsProbeJson json,
    RequestCqrsProbeRecords records, IOptions<RequestProbeExecutionOptions> options, Lock sync,
    Func<RequestCqrsProbeSnapshot> snapshot, Action<string, byte[]> write)
{
    internal void Read(string path, string name, List<RequestCqrsProbeLiveRecord> values, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = json.ReadLive(bytes);
        if (value.SessionId != session || RequestCqrsProbeLiveValidation.Name(value) != name)
        { throw Invalid(); }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
        values.Add(value);
    }

    internal void Acknowledge(RequestCqrsProbeActivationRecord witness, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            _ = snapshot();
            var values = Directory.EnumerateFiles(root).Where(path => RequestCqrsProbeLiveValidation.IsName(Path.GetFileName(path)))
                .Select(path => json.ReadLive(RequestCqrsProbeFiles.ReadRecord(path, options))).ToArray();
            foreach (var challenge in values.Where(value => value.Kind == RequestCqrsProbeLiveProtocol.ChallengeKind
                && RequestCqrsProbeLiveValidation.Witness(value) == witness).OrderBy(value => value.Step))
            {
                if (values.Any(value => value.Kind == RequestCqrsProbeLiveProtocol.AckKind
                    && value.ArmId == challenge.ArmId && value.RequestId == challenge.RequestId && value.Step == challenge.Step))
                { continue; }
                cancellationToken.ThrowIfCancellationRequested();
                var ack = challenge with
                {
                    Kind = RequestCqrsProbeLiveProtocol.AckKind,
                    ChallengeSha256 = Hash(RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, RequestCqrsProbeLiveValidation.Name(challenge)), options))
                };
                var name = RequestCqrsProbeLiveValidation.Name(ack);
                var bytes = json.WriteLive(ack);
                write(Path.Combine(root, name), bytes);
                records.RegisterImmutable(name, bytes);
            }
        }
    }

    internal static void RequireInventory(List<RequestCqrsProbeLiveRecord> values,
        List<RequestCqrsProbeActivationRecord> witnesses, List<RequestCqrsProbeMarkerRecord> markers,
        string root, IOptions<RequestProbeExecutionOptions> options)
    {
        foreach (var value in values)
        {
            var witness = RequestCqrsProbeLiveValidation.Witness(value);
            if (!witnesses.Contains(witness))
            { throw Invalid(); }
            RequestCqrsProbeActivationValidation.RequireMarker(witness, markers);
            if (value.Step == RequestCqrsProbeLiveProtocol.AfterReplacement
                && !values.Any(candidate => candidate.ArmId == value.ArmId && candidate.RequestId == value.RequestId
                    && candidate.Step == RequestCqrsProbeLiveProtocol.BeforeReplacement && candidate.Kind == RequestCqrsProbeLiveProtocol.AckKind))
            { throw Invalid(); }
            if (value.Kind != RequestCqrsProbeLiveProtocol.AckKind)
            { continue; }
            var expected = value with
            {
                Kind = RequestCqrsProbeLiveProtocol.ChallengeKind,
                ChallengeSha256 = RequestCqrsProbeLiveProtocol.EmptyDigest
            };
            if (!values.Contains(expected) || value.ChallengeSha256 != Hash(RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, RequestCqrsProbeLiveValidation.Name(expected)), options)))
            { throw Invalid(); }
        }
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
