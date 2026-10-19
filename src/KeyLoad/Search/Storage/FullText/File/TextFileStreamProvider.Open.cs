using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class TextFileStreamProvider
{

    private IFileStream OpenOwned(string full, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        => TextFileOpening.Open(inner, pathAccess, resources, fileBufferBytes, full, mode, access, share, bufferSize, options);

}
