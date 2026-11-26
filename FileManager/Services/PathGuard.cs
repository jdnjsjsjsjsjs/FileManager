namespace FileManager.Services;

public sealed class SecurityViolationException(string message) : Exception(message);

public static class PathGuard
{
    private static readonly StringComparison Cmp =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new ArgumentException("Пустой путь");
        if (relative.Contains('\0'))
            throw new SecurityViolationException("Недопустимый символ в пути");
        if (Path.IsPathRooted(relative))
            throw new SecurityViolationException("Абсолютные пути запрещены");

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        var prefix = Path.EndsInDirectorySeparator(rootFull) ? rootFull : rootFull + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, Cmp))
            throw new SecurityViolationException($"Выход за пределы рабочей папки: {relative}");

        if (new FileInfo(full).LinkTarget is not null)
            throw new SecurityViolationException("Символические ссылки запрещены");

        return full;
    }
}
