using System.Text;
using FileManager.Models;

namespace FileManager.Services;

public sealed class FileService
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly AuditService _audit;
    private readonly User _user;

    public string Root { get; }

    public FileService(AuditService audit, User user, string baseDir)
    {
        _audit = audit;
        _user = user;
        Root = Path.GetFullPath(Path.Combine(baseDir, "storage", user.Username));
        Directory.CreateDirectory(Root);
    }

    public string Resolve(string rel) => PathGuard.Resolve(Root, rel);

    public string Rel(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

    public IEnumerable<string> List() =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(Rel).OrderBy(x => x);

    public string ReadText(string rel)
    {
        var full = Resolve(rel);
        string text;
        long length;
        using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            length = fs.Length;
            if (length > MaxFileBytes)
                throw new InvalidOperationException("Файл слишком большой (лимит 5 МБ)");
            using var sr = new StreamReader(fs, Encoding.UTF8);
            text = sr.ReadToEnd();
        }
        _audit.LogFile(_user.Id, Rel(full), Path.GetFileName(full), length, "read");
        return text;
    }

    public void WriteText(string rel, string content, bool overwrite)
    {
        var bytes = new UTF8Encoding(false).GetBytes(content);
        if (bytes.Length > MaxFileBytes)
            throw new InvalidOperationException("Содержимое слишком большое (лимит 5 МБ)");

        var full = Resolve(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var op = "create";
        FileStream fs;
        try
        {
            fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(full))
        {
            if (!overwrite) throw new InvalidOperationException("Файл уже существует");
            fs = new FileStream(full, FileMode.Truncate, FileAccess.Write, FileShare.None);
            op = "modify";
        }

        using (fs) fs.Write(bytes, 0, bytes.Length);
        Record(full, op);
    }

    public void Delete(string rel)
    {
        var full = Resolve(rel);
        if (!File.Exists(full)) throw new FileNotFoundException("Файл не найден");
        File.Delete(full);
        Record(full, "delete");
    }

    public void Record(string full, string op, string? details = null)
    {
        long size = op == "delete" ? 0 : new FileInfo(full).Length;
        _audit.LogFile(_user.Id, Rel(full), Path.GetFileName(full), size, op, details);
    }
}
