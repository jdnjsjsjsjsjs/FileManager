using System.IO.Compression;

namespace FileManager.Services;

public sealed class ZipService(FileService files)
{
    private const int MaxEntries = 1000;
    private const long MaxEntryBytes = 20L * 1024 * 1024;
    private const long MaxTotalBytes = 100L * 1024 * 1024;
    private const int MaxRatio = 100;

    public void Create(string zipRel, IReadOnlyList<string> sourcesRel)
    {
        if (sourcesRel.Count == 0) throw new ArgumentException("Не указаны файлы для архивации");
        if (sourcesRel.Count > MaxEntries) throw new InvalidOperationException("Слишком много файлов");

        var zipFull = files.Resolve(zipRel);
        Directory.CreateDirectory(Path.GetDirectoryName(zipFull)!);

        var fs = new FileStream(zipFull, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (fs)
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var rel in sourcesRel)
                {
                    var full = files.Resolve(rel);
                    if (full == zipFull) continue;
                    if (!File.Exists(full)) throw new FileNotFoundException($"Не найден файл: {rel}");
                    zip.CreateEntryFromFile(full, files.Rel(full), CompressionLevel.Optimal);
                }
            }
        }
        catch
        {
            File.Delete(zipFull);
            throw;
        }
        files.Record(zipFull, "create", $"zip, файлов: {sourcesRel.Count}");
    }

    public int Extract(string zipRel, string destRel)
    {
        var zipFull = files.Resolve(zipRel);
        var destRoot = files.Resolve(destRel);
        var extracted = new List<string>();
        long total = 0;

        try
        {
            using var zip = ZipFile.OpenRead(zipFull);

            if (zip.Entries.Count > MaxEntries)
                throw new SecurityViolationException("Слишком много файлов в архиве");
            long declared = 0;
            foreach (var e in zip.Entries)
            {
                if (e.Length > MaxEntryBytes)
                    throw new SecurityViolationException($"Файл в архиве слишком большой: {e.FullName}");
                if (e.CompressedLength > 0 && e.Length / e.CompressedLength > MaxRatio)
                    throw new SecurityViolationException($"Подозрительная степень сжатия (zip-бомба): {e.FullName}");
                declared += e.Length;
            }
            if (declared > MaxTotalBytes)
                throw new SecurityViolationException("Суммарный размер распакованных данных превышает лимит");

            Directory.CreateDirectory(destRoot);
            
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;   // запись-каталог

                var target = PathGuard.Resolve(destRoot, e.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using var input = e.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                extracted.Add(target);

                var buf = new byte[81920];
                long entryBytes = 0;
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    entryBytes += n;
                    total += n;
                    if (entryBytes > MaxEntryBytes || total > MaxTotalBytes)
                        throw new SecurityViolationException("Превышен лимит размера распакованных данных (zip-бомба?)");
                    output.Write(buf, 0, n);
                }
            }
        }
        catch
        {
            foreach (var f in extracted)
                try { File.Delete(f); } catch { /* best effort */ }
            throw;
        }

        foreach (var f in extracted)
            files.Record(f, "create", $"извлечено из {files.Rel(zipFull)}");
        return extracted.Count;
    }
}
