using Microsoft.Extensions.Options;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsControlledLifetimeFiles(string root, string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records, Lock sync,
    Func<RequestCqrsProbeSnapshot> readSnapshot)
{
    internal RequestCqrsControlledLifetimeInventory Inventory() => new(sessionId, options, records);

    internal RequestCqrsControlledLifetimePlan RequirePlan(RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, readSnapshot());
            var name = RequestCqrsControlledLifetimeNames.PlanName(claim.Arm.Record.ArmId);
            var bytes = Read(name);
            var plan = RequestCqrsControlledLifetimeCodec.ReadPlan(bytes);
            RequestCqrsControlledLifetimeInventory.RequireArm(plan, claim.Arm.Record);
            records.RegisterImmutable(name, bytes);
            return plan;
        }
    }

    internal void Publish(RequestCqrsControlledLifetimeStepWitness value, byte[]? carrier,
        byte[]? request, byte[]? reply)
    {
        lock (sync)
        {
            _ = readSnapshot();
            var payload = new List<(string Name, byte[] Bytes)>();
            if (carrier is not null)
            {
                payload.AddRange(Parts(value, CarrierRole, carrier));
            }
            if (request is not null)
            {
                payload.AddRange(Parts(value, RequestRole, request));
            }
            if (reply is not null)
            {
                payload.AddRange(Parts(value, ReplyRole, reply));
            }
            payload.Add((RequestCqrsControlledLifetimeNames.StepName(value.ArmId, value.RequestId, value.Step),
                RequestCqrsControlledLifetimeCodec.WriteWitness(value)));
            var snapshot = readSnapshot();
            if (checked(snapshot.FileCount + payload.Count) > options.Value.MaximumFiles
                || checked(snapshot.AggregateBytes + payload.Sum(part => (long)part.Bytes.Length)) > options.Value.MaximumAggregateBytes)
            {
                throw InvalidProof();
            }
            foreach (var part in payload)
            {
                RequestCqrsProbeAtomicFiles.Write(root, Path.Combine(root, part.Name), part.Bytes, options, readSnapshot);
                records.RegisterImmutable(part.Name, part.Bytes);
            }
            _ = readSnapshot();
        }
    }

    internal bool RequireAck(RequestCqrsProbeClaim claim, int step)
    {
        lock (sync)
        {
            var snapshot = readSnapshot();
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
            if (snapshot.Releases.Any(value => value.ArmId == claim.Arm.Record.ArmId))
            {
                throw InvalidProof();
            }
            var name = RequestCqrsControlledLifetimeNames.AckName(claim.Arm.Record.ArmId, claim.Identity.RequestId, step);
            if (!File.Exists(Path.Combine(root, name)))
            {
                return false;
            }
            var ack = RequestCqrsControlledLifetimeCodec.ReadAck(Read(name));
            if (ack.SessionId != sessionId || ack.ArmId != claim.Arm.Record.ArmId
                || ack.RequestId != claim.Identity.RequestId || ack.Step != step)
            {
                throw InvalidProof();
            }
            return true;
        }
    }

    internal byte[] ReadDonor(RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            var plan = RequirePlan(claim);
            var donorId = plan.SourceArmId ?? throw InvalidProof();
            var inventory = Inventory();
            var snapshot = readSnapshot();
            var entries = Directory.EnumerateFileSystemEntries(root).Take(checked(options.Value.MaximumFiles + IndexStep)).ToArray();
            if (entries.Length > options.Value.MaximumFiles)
            {
                throw InvalidProof();
            }
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in entries.Where(path => RequestCqrsControlledLifetimeNames.IsName(Path.GetFileName(path))))
            {
                _ = RequestCqrsProbeSnapshotFiles.ValidateEntry(path, Path.GetFileName(path), options);
                inventory.Read(path, Path.GetFileName(path), present);
            }
            inventory.Require(snapshot.Arms, snapshot.Markers);
            var donor = inventory.Donor(claim, donorId, snapshot.Markers);
            _ = readSnapshot();
            return donor;
        }
    }

    private IEnumerable<(string Name, byte[] Bytes)> Parts(RequestCqrsControlledLifetimeStepWitness value, string role, byte[] bytes)
        => RequestCqrsControlledLifetimeParts.Split(value.ArmId, value.RequestId, value.Step, role, bytes, options.Value.MaximumRecordBytes);
    private byte[] Read(string name) => RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, name), options);
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
