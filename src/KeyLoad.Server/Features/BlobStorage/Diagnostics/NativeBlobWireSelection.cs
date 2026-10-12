using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireSelection(NativeBlobWireFiles files, NativeBlobWireCodec codec,
    NativeBlobWireState state, NativeBlobWireClaims claims)
{
    internal bool Select(RemoteControlledBlobCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = state.Read();
        if (snapshot.Arms is not { } arms || snapshot.ArmsDigest is not { } armsDigest) { return false; }
        var arm = NativeBlobWireScope.Match(snapshot.Owner, arms, call);
        if (arm is null) { return false; }
        var expected = NativeBlobWireClaims.Create(snapshot.Owner, snapshot.OwnerDigest, armsDigest, arm, call);
        claims.Require(snapshot.Owner, snapshot.OwnerDigest, armsDigest, arm, expected);
        var name = NativeBlobWireNames.Claim(arm.Identity);
        var failures = new List<Exception>();
        var selected = false;
        var acquired = false;
        try
        {
            try
            {
                using (files.Publication())
                {
                    acquired = true;
                    ServerFailureObserver.Observe(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (files.Exists(name)) { RequireExisting(snapshot, arm, name); }
                        else
                        {
                            files.WriteLocked(name, codec.Encode(expected));
                            selected = true;
                        }
                    }, failures);
                }
            }
            catch (IOException) when (!acquired)
            {
                if (!files.Exists(name)) { throw; }
                RequireExisting(snapshot, arm, name);
            }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        return selected;
    }

    internal (NativeBlobWireSnapshot Snapshot, NativeBlobWireClaim Claim, string Digest) RequireSelected(
        RemoteControlledBlobCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = state.Read();
        if (snapshot.Arms is not { } arms || snapshot.ArmsDigest is not { } armsDigest)
        { throw Invalid(); }
        var arm = NativeBlobWireScope.Match(snapshot.Owner, arms, call) ?? throw Invalid();
        var bytes = files.Read(NativeBlobWireNames.Claim(arm.Identity));
        var claim = codec.Read<NativeBlobWireClaim>(bytes);
        claims.Require(snapshot.Owner, snapshot.OwnerDigest, armsDigest, arm, claim);
        NativeBlobWireClaims.RequireOwn(claim, call);
        return (snapshot, claim, NativeBlobWireCodec.Digest(bytes));
    }

    private void RequireExisting(NativeBlobWireSnapshot snapshot, NativeBlobWireArm arm, string name)
        => claims.Require(snapshot.Owner, snapshot.OwnerDigest, snapshot.ArmsDigest!, arm,
            codec.Read<NativeBlobWireClaim>(files.Read(name)));
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
