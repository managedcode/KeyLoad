namespace KeyLoad.Server.Features.Search;

/// <summary>Borrows partition-owned admission without transferring maintenance disposal.</summary>
internal interface INativeTextSharedReadAdmission
{
    NativeTextSelectedReadLease EnterBootstrapRead();
}
