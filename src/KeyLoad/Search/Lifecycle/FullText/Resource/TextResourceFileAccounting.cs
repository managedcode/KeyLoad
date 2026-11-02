using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextResourceFileAccounting
{
    private const long AbsentFileLength = 0;
    private const long EmptyFileLength = 0;
    private const long NoAdditionalBytes = 0;
    internal static void Check(Dictionary<object, TextResourceFileReservation> /* KLD0041: One reservation ledger preserves string value equality for canonical paths and reference identity for TextOpenFileGroup instances; both keys are existing references. */ reservations,
        ref int files, ref long bytes, IOptions<TextExecutionOptions> options)
    {
        foreach (var reservation in reservations.Values)
        {
            if ((reservation.Key /* KLD0041: This accounting key is either a canonical path string or the existing TextOpenFileGroup reference; distinguish shared open-file accounting without adding a key wrapper. */) is TextOpenFileGroup)
            {
                continue;
            }
            var exists = File.Exists(reservation.Path);
            if (exists)
            {
                TextFileIO.VerifyRegularFile(reservation.Path);
            }
            var physicalLength = exists ? new FileInfo(reservation.Path).Length : AbsentFileLength;
            var observedLength = reservation.ObserveLength();
            if (observedLength < EmptyFileLength || reservation.TargetLength < EmptyFileLength)
            {
                throw TextErrors.Corrupt();
            }
            var projected = Math.Max(observedLength, reservation.TargetLength);
            var additional = Math.Max(NoAdditionalBytes, projected - physicalLength);
            if ((!exists && files >= options.Value.MaximumFiles)
                || additional > options.Value.MaximumDiskBytes - bytes)
            {
                throw TextErrors.BoundExceeded();
            }
            if (!exists)
            {
                files++;
            }
            bytes += additional;
        }
    }
}
