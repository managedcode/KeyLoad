namespace KeyLoad.Server.Search;

/// <summary>Borrows partition-owned admission without transferring maintenance disposal.</summary>
internal interface ITextSharedReadAdmission
{
    TextSelectedReadLease EnterBootstrapRead();
}
