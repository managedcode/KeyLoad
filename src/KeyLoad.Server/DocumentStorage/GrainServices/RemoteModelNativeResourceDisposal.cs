using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeResourceDisposal
{
    internal static void Capture(Action dispose, List<Exception> failures)
    {
        try
        {
            dispose();
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

}
