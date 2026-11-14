using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FileManager.Services;

public sealed record Note(string Title, string Content, int Priority);

public sealed class JsonXmlService(FileService files)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1_000_000,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true
    };

    public void SaveJson(string rel, Note note, bool overwrite)
    {
        Validate(note);
        files.WriteText(rel, JsonSerializer.Serialize(note, JsonOpts), overwrite);
    }

    public Note LoadJson(string rel)
    {
        var text = files.ReadText(rel);
        Note? note;
        try { note = JsonSerializer.Deserialize<Note>(text, JsonOpts); }
        catch (JsonException ex) { throw new InvalidDataException("Некорректный JSON: " + ex.Message); }
        if (note is null) throw new InvalidDataException("Пустой JSON");
        Validate(note);
        return note;
    }

    public void SaveXml(string rel, Note note, bool overwrite)
    {
        Validate(note);
        string xml;
        try
        {
            xml = new XDocument(new XElement("note",
                new XElement("title", note.Title),
                new XElement("content", note.Content),
                new XElement("priority", note.Priority))).ToString();
        }
        catch (ArgumentException) { throw new InvalidDataException("Текст содержит недопустимые для XML символы"); }
        files.WriteText(rel, xml, overwrite);
    }

    public Note LoadXml(string rel)
    {
        var text = files.ReadText(rel);
        try
        {
            using var sr = new StringReader(text);
            using var xr = XmlReader.Create(sr, XmlSettings);
            var root = XDocument.Load(xr).Root;
            if (root is null || root.Name != "note") throw new InvalidDataException("Ожидался корневой элемент <note>");

            var title = root.Element("title")?.Value ?? throw new InvalidDataException("Нет <title>");
            var content = root.Element("content")?.Value ?? throw new InvalidDataException("Нет <content>");
            if (!int.TryParse(root.Element("priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prio))
                throw new InvalidDataException("Некорректный <priority>");

            var note = new Note(title, content, prio);
            Validate(note);
            return note;
        }
        catch (XmlException ex) { throw new InvalidDataException("Некорректный XML: " + ex.Message); }
    }

    private static void Validate(Note n)
    {
        if (string.IsNullOrWhiteSpace(n.Title) || n.Title.Length > 100)
            throw new InvalidDataException("Title: от 1 до 100 символов");
        if (n.Content is null || n.Content.Length > 10_000)
            throw new InvalidDataException("Content: до 10000 символов");
        if (n.Priority is < 1 or > 5)
            throw new InvalidDataException("Priority: число от 1 до 5");
    }
}
