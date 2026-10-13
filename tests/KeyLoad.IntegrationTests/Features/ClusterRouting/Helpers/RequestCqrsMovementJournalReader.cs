using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsMovementJournalReader
{
    internal static RequestCqrsMovementJournalRecord Read(RequestCqrsProbeFixture fixture,
        RequestCqrsProbeMarkerRecord marker)
    {
        RequestCqrsMovementJournalRecord? selected = null;
        var expectedName = RequestCqrsMovementJournalInventory.Name(marker.ArmId, marker.RequestId);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var owned = fixture.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            foreach (var path in RequestCqrsProbeFileValidation.ValidateContents(owned.Directory))
            {
                if (Path.GetFileName(path) != expectedName)
                { continue; }
                if (selected is not null)
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                var value = fixture.Json.Read(RequestCqrsProbeFileStore.ReadRecord(path),
                    RequestCqrsMovementJournalInventory.Fields, fixture.Json.MovementJournalTypeInfo);
                RequestCqrsMovementJournalInventory.Require(value);
                RequestCqrsMovementJournalInventory.RequireMarker(value, marker);
                RequestCqrsMovementJournalInventory.RequireArm(value,
                    fixture.Json.ReadArm(fixture.ArmFor(marker.ArmId).Bytes));
                if (fixture.ArmFor(marker.ArmId).GateVoter != node)
                { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch); }
                selected = value;
            }
        }
        return selected ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
    }
}
