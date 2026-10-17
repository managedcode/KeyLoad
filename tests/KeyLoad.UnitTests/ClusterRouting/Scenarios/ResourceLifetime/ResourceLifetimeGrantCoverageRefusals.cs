namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Rejects incomplete and substituted lifetime coverage without changing either owner.</summary>
internal static class ResourceLifetimeGrantCoverageRefusals
{
    private const int FirstLifetime = 0;
    private const string OtherResource = "not-the-admitted-resource";
    private const string OtherPartition = "not-the-admitted-partition";

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        await CollectionsAsync(state, token);
        await IdentityAsync(state, token);
    }

    private static async Task CollectionsAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var proof = state.Proof;
        var coverage = proof.ControlProof.Coverage;
        var first = coverage.Lifetimes.First();
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = default
                }
            }
        }, ErrorCode.Corruption, token);
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = []
                }
            }
        }, ErrorCode.Corruption, token);
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = coverage.Lifetimes.Add(first)
                }
            }
        }, ErrorCode.Corruption, token);
    }

    private static async Task IdentityAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var proof = state.Proof;
        var coverage = proof.ControlProof.Coverage;
        var first = coverage.Lifetimes.First();
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = coverage.Lifetimes.Add(first with
                    {
                        ResourceName = OtherResource
                    })
                }
            }
        }, ErrorCode.Corruption, token);
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Partition = coverage.Partition with
                    {
                        PartitionKey = OtherPartition
                    }
                }
            }
        }, ErrorCode.Corruption, token);
        await ResourceLifetimeGrantRefusals.RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = coverage.Lifetimes.SetItem(FirstLifetime, first with
                    {
                        Incarnation = new(ResourceLifetimeGrantRefusals.OtherFingerprint)
                    })
                }
            }
        }, ErrorCode.Corruption, token);
    }
}
