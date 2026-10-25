using System.Xml;

namespace KeyLoad.FunctionalTests.CodeQuality;

internal static class CoverageReportXmlRoot
{
    private const string RootName = "coverage";

    internal static XmlElement LoadRoot(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes || info.LinkTarget is not null)
        {
            throw new InvalidDataException(CoverageLineUnionAssertions.InvalidReport);
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes
        };
        using var reader = XmlReader.Create(path, settings);
        var document = new XmlDocument
        {
            XmlResolver = null
        };
        document.Load(reader);
        return document.DocumentElement is { LocalName: RootName } root ? root : throw new InvalidDataException(CoverageLineUnionAssertions.InvalidReport);
    }

}
