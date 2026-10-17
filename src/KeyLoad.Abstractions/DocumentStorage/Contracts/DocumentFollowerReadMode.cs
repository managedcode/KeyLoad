namespace KeyLoad;

/// <summary>The explicit supported follower data consistency contract.</summary>
public enum DocumentFollowerReadMode
{
    /// <summary>One captured committed local data cut, projected only under fresh persisted authority.</summary>
    FollowerCommittedSnapshot
}
