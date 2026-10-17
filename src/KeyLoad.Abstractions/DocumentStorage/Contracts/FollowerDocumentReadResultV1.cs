namespace KeyLoad;

/// <summary>The explicit supported follower data consistency contract.</summary>
public enum DocumentFollowerReadMode
{
    /// <summary>One captured committed local data cut, projected only under fresh persisted authority.</summary>
    FollowerCommittedSnapshot
}

/// <summary>Returns explicit data and current-authority cuts without claiming latest or offline read authority.</summary>
/// <param name="Version">The result format version, one.</param>
/// <param name="Mode">The explicitly selected follower consistency mode.</param>
/// <param name="ReplicaId">Actual native follower voter identity.</param>
/// <param name="CapturedTerm">Actual native term bracketing the data capture.</param>
/// <param name="AuthorityTerm">Actual native follower term after the fresh authorization barrier.</param>
/// <param name="DataToken">Exact committed cut from which the document was captured.</param>
/// <param name="AuthorizationToken">Exact final freshly authorized committed cut.</param>
/// <param name="PositionLag">Difference between authorization and data positions, including control entries.</param>
/// <param name="PolicyEpoch">Current persisted principal policy epoch at authorization.</param>
/// <param name="Document">Captured document projected under current policy, or current-authorized absence.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(FollowerDocumentAliases.Result)]
public sealed record FollowerDocumentReadResultV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] DocumentFollowerReadMode Mode,
    [property: Orleans.Id(2)] string ReplicaId,
    [property: Orleans.Id(3)] long CapturedTerm,
    [property: Orleans.Id(4)] long AuthorityTerm,
    [property: Orleans.Id(5)] CommitToken DataToken,
    [property: Orleans.Id(6)] CommitToken AuthorizationToken,
    [property: Orleans.Id(7)] long PositionLag,
    [property: Orleans.Id(8)] long PolicyEpoch,
    [property: Orleans.Id(9)] DocumentResult? Document);
