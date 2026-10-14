using KeyLoad.AppHost.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutOriginalArm
{
    private const int ExtraInventoryEntry = 1;
    internal static void RequireOwners(RequestCqrsProbeFixture controls)
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(controls.Root);
        var entries = Directory.EnumerateFileSystemEntries(controls.Root)
            .Take(TwoRf3MembershipProtocol.NodeCount + ExtraInventoryEntry).ToArray();
        if (entries.Length != TwoRf3MembershipProtocol.NodeCount)
        { throw Invalid(); }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var original = controls.NodeFor(node);
            if (original.Directory != Path.Combine(controls.Root, node)
                || !entries.Contains(original.Directory, StringComparer.Ordinal))
            { throw Invalid(); }
            RequestCqrsProbeFileValidation.ValidateContents(original.Directory);
            RequestCqrsProbeFileStore.VerifyOwnerFile(original.Directory, original.OwnerBytes);
            var owner = controls.Json.ReadOwner(original.OwnerBytes);
            if (owner.SessionId != controls.SessionId || owner.Voter != TwoRf3ClusterResources.Origin(node))
            { throw Invalid(); }
        }
    }

    internal static byte[] Read(RequestCqrsProbeFixture controls, Guid armId)
    {
        RequireOwners(controls);
        var state = controls.ArmFor(armId);
        if (armId == Guid.Empty || state.ArmId != armId || state.Retired || state.ProcessOwnerJoined)
        { throw Invalid(); }
        var bytes = state.Bytes.ToArray();
        var arm = controls.Json.ReadArm(bytes);
        RequestCqrsProbeArmValidator.ValidateDecoded(arm, armId, state.PrincipalId,
            state.CommandId, state.ReadKind, state.Phase, state.Action);
        if (arm.SessionId != controls.SessionId)
        { throw Invalid(); }
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var original = controls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyExactFile(original.Directory,
                RequestCqrsProbeFileNames.Arm(armId), bytes);
        }
        if (state.Retired || state.ProcessOwnerJoined || !state.Bytes.AsSpan().SequenceEqual(bytes))
        { throw Invalid(); }
        return bytes;
    }

    private static IOException Invalid() => new(RequestCqrsProbeFixtureProtocol.InvalidArm);
}
