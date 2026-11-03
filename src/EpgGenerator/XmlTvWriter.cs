using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EpgGenerator;

public static class XmlTvWriter
{
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static void Write(
        IReadOnlyList<MergedChannel> channels,
        AliasConfig aliasConfig,
        string xmlPath,
        string gzPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(xmlPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmpXml = xmlPath + ".tmp";
        var tmpGz = gzPath + ".tmp";

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  "
        };

        using (var fs = File.Create(tmpXml))
        using (var writer = XmlWriter.Create(fs, settings))
        {
            writer.WriteStartDocument();
            writer.WriteDocType("tv", null, "xmltv.dtd", null);
            writer.WriteStartElement("tv");
            writer.WriteAttributeString("generator-info-name", "EPG-AutoAdjusted");
            writer.WriteAttributeString("date", FormatXmlTvTime(DateTimeOffset.UtcNow));

            foreach (var channel in channels.OrderBy(c => c.BaseName, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartElement("channel");
                writer.WriteAttributeString("id", channel.Id);

                foreach (var displayName in ChannelNormalizer.BuildDisplayNames(channel, aliasConfig))
                {
                    writer.WriteElementString("display-name", displayName);
                }

                if (!string.IsNullOrWhiteSpace(channel.LogoUrl))
                {
                    writer.WriteStartElement("icon");
                    writer.WriteAttributeString("src", channel.LogoUrl);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // </channel>
            }

            foreach (var channel in channels.OrderBy(c => c.BaseName, StringComparer.OrdinalIgnoreCase))
            {
                var orderedProgrammes = channel.Programmes
                    .OrderBy(p => p.Start)
                    .GroupBy(p => p.Start)
                    .Select(g => g.First());

                foreach (var prog in orderedProgrammes)
                {
                    writer.WriteStartElement("programme");
                    writer.WriteAttributeString("start", FormatXmlTvTime(prog.Start));
                    writer.WriteAttributeString("stop", FormatXmlTvTime(prog.Stop));
                    writer.WriteAttributeString("channel", channel.Id);

                    writer.WriteStartElement("title");
                    writer.WriteAttributeString("lang", "pt");
                    writer.WriteString(prog.Title);
                    writer.WriteEndElement();

                    if (!string.IsNullOrWhiteSpace(prog.SubTitle))
                    {
                        writer.WriteStartElement("sub-title");
                        writer.WriteAttributeString("lang", "pt");
                        writer.WriteString(prog.SubTitle);
                        writer.WriteEndElement();
                    }

                    if (!string.IsNullOrWhiteSpace(prog.Description))
                    {
                        writer.WriteStartElement("desc");
                        writer.WriteAttributeString("lang", "pt");
                        writer.WriteString(prog.Description);
                        writer.WriteEndElement();
                    }

                    if (!string.IsNullOrWhiteSpace(prog.Category))
                    {
                        writer.WriteStartElement("category");
                        writer.WriteAttributeString("lang", "pt");
                        writer.WriteString(prog.Category);
                        writer.WriteEndElement();
                    }

                    if (!string.IsNullOrWhiteSpace(prog.IconUrl))
                    {
                        writer.WriteStartElement("icon");
                        writer.WriteAttributeString("src", prog.IconUrl);
                        writer.WriteEndElement();
                    }

                    if (prog.Season.HasValue || prog.Episode.HasValue)
                    {
                        int s = (prog.Season ?? 1) - 1;
                        int e = (prog.Episode ?? 1) - 1;
                        writer.WriteStartElement("episode-num");
                        writer.WriteAttributeString("system", "xmltv_ns");
                        writer.WriteString($"{Math.Max(0, s)}.{Math.Max(0, e)}.");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement(); // </programme>
                }
            }

            writer.WriteEndElement(); // </tv>
            writer.WriteEndDocument();
        }

        using (var sourceStream = File.OpenRead(tmpXml))
        using (var targetStream = File.Create(tmpGz))
        using (var gzip = new GZipStream(targetStream, CompressionLevel.Optimal))
        {
            sourceStream.CopyTo(gzip);
        }

        File.Move(tmpXml, xmlPath, overwrite: true);
        File.Move(tmpGz, gzPath, overwrite: true);
    }

    public static string FormatXmlTvTime(DateTimeOffset dto)
    {
        var brt = dto.ToOffset(BrasiliaOffset);
        return $"{brt:yyyyMMddHHmmss} -0300";
    }
}

