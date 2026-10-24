using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class AnnCompletedPublication
{
    internal static AnnManifest Save(AnnGenerationOwner owner, AnnRootLease root,
        List<AnnGenerationSlot> slots, AnnExecutionOptions options, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, AnnMaintenanceRequest request, AnnSeed seed, PackedAnnIndex index,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget, long? checkpointAfter)
    {
        var leaf = AnnProtocol.GenerationPrefix + Guid.NewGuid().ToString(AnnProtocol.GuidFormat);
        var stageStorage = AnnDiskAdmission.ForStage(root.Root, root.Receipt, storage, options);
        root.Enroll(leaf, options);
        var published = false;
        try
        {
            var manifest = AnnCanonicalSource.Manifest(request, seed, leaf, policy.Value)
                with
            {
                CheckpointIntent = intent,
                ReplayAfter = checkpointAfter ?? seed.ProjectionCheckpoint ?? seed.Cut.OutboxTail
            };
            var saved = AnnGenerationFiles.Save(root.Root, root.Receipt, manifest, index, stageStorage, options, budget);
            budget.Check();
            var key = new AnnOwnedKey(request.Consumer, request.IndexGeneration);
            var pending = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
            root.Publish(key, saved.Pointer, options);
            published = true;
            if (pending is not null)
            {
                root.Remove(pending.Pointer.GenerationLeaf, options);
            }
            foreach (var slot in slots.Where(item => !item.Retired && AnnSlotRetention.SameKey(item.Manifest, key)))
            {
                slot.Retire();
            }
            slots.Add(new(owner, saved.Manifest, index));
            AnnSlotRetention.RetireUnpinned(root, slots, options);
            return saved.Manifest;
        }
        catch (Exception primary)
        {
            if (!published)
            {
                try
                {
                    root.Remove(leaf, options);
                }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }
            throw;
        }
    }

}
