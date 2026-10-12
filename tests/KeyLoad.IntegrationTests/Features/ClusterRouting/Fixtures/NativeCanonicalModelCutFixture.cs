using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns only additional private control files; the original Wave owns every native resource.</summary>
internal sealed class NativeCanonicalModelCutFixture
{
    private readonly System.Threading.Lock sync = new();
    private readonly TwoRf3MembershipWave owner;
    private readonly RequestCqrsProbeFixture original;
    private readonly NativeCanonicalModelCutFiles files;
    private readonly Dictionary<Guid, byte[]> arms = [];
    private bool admissionStopped;
    private bool configured;
    private bool retained;
    private bool cleaned;

    private NativeCanonicalModelCutFixture(TwoRf3MembershipWave wave, NativeCanonicalModelCutFiles files)
    { owner = wave; original = wave.QueryControls; this.files = files; }

    internal string Root => files.Root;
    internal string SessionId => original.SessionId;

    internal static NativeCanonicalModelCutFixture Create(TwoRf3MembershipWave wave, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(wave);
        if (!wave.remoteDocumentReads || !wave.remotePartitionQueries || !wave.queryProbe
            || !wave.protectedDocuments || wave.queryControls is null
            || wave.nativeMembershipDiagnostics || Path.GetFullPath(dataRoot) != Path.GetFullPath(wave.OwnedDataRoot))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        NativeCanonicalModelCutOriginalArm.RequireOwners(wave.QueryControls);
        return new(wave, NativeCanonicalModelCutFiles.Create(dataRoot, wave.QueryControls));
    }

    internal void Configure(IDistributedApplicationTestingBuilder builder)
    {
        lock (sync)
        {
            RequireAdmission();
            if (configured)
            { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
            NativeCanonicalModelCutOriginalArm.RequireOwners(original);
            files.RequireInventory(arms);
            try { NativeCanonicalModelCutMounts.Configure(builder, files, SessionId); }
            catch (Exception) { admissionStopped = true; retained = true; throw; }
            configured = true;
        }
    }

    internal void MirrorArm(Guid originalArmId)
    {
        lock (sync)
        {
            RequireAdmission();
            if (!configured)
            { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
            try { MirrorOriginal(originalArmId); }
            catch (Exception) { admissionStopped = true; retained = true; throw; }
        }
    }

    private void MirrorOriginal(Guid armId)
    {
        var bytes = NativeCanonicalModelCutOriginalArm.Read(original, armId);
        files.RequireInventory(arms);
        if (arms.TryGetValue(armId, out var existing))
        {
            if (!existing.AsSpan().SequenceEqual(bytes))
            { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
            return;
        }
        if (arms.Count >= RequestCqrsProbeFixtureProtocol.MaximumArms)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.FileLimitExceeded); }
        RequestCqrsProbeBroadcast.WriteIdentical(NativeCanonicalModelCutFiles.Nodes,
            files.Directories, files.Owners, RequestCqrsProbeFileNames.Arm(armId), bytes);
        arms.Add(armId, bytes);
        if (!NativeCanonicalModelCutOriginalArm.Read(original, armId).AsSpan().SequenceEqual(bytes))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
        files.RequireInventory(arms);
    }

    internal void StopAdmission()
    { lock (sync) { admissionStopped = true; } }

    internal void RetainEvidence()
    { lock (sync) { retained = true; admissionStopped = true; } }

    internal Task ReopenAfterOwnersJoinedAsync(TwoRf3MembershipWave wave)
    {
        lock (sync)
        {
            RequireOwner(wave);
            if (!admissionStopped || cleaned || retained)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.AdmissionStopped); }
            NativeCanonicalModelCutFiles.RequireJoinedOwners(wave);
            NativeCanonicalModelCutOriginalArm.RequireOwners(original);
            files.RequireInventory(arms);
            admissionStopped = false;
            configured = false;
            return Task.CompletedTask;
        }
    }

    internal Task DisposeAfterOwnersJoinedAsync(TwoRf3MembershipWave wave)
    {
        lock (sync)
        {
            RequireOwner(wave);
            if (!admissionStopped)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
            if (cleaned)
            { return Task.CompletedTask; }
            NativeCanonicalModelCutFiles.RequireJoinedOwners(wave);
            if (retained)
            { return Task.CompletedTask; }
            try { files.Delete(arms); cleaned = true; }
            catch (Exception) { retained = true; throw; }
            return Task.CompletedTask;
        }
    }

    private void RequireAdmission()
    {
        RequireOwner(owner);
        if (admissionStopped || cleaned || retained)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.AdmissionStopped); }
    }

    private void RequireOwner(TwoRf3MembershipWave wave)
    {
        if (!ReferenceEquals(owner, wave) || !ReferenceEquals(original, wave.queryControls))
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
    }
}
