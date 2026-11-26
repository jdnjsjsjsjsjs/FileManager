using System.Text;
using FileManager.Data;
using FileManager.Models;
using FileManager.Services;

namespace FileManager;

internal static class Program
{
    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var baseDir = Directory.GetCurrentDirectory();
        var db = new Database(Path.Combine(baseDir, "filemanager.db"));
        db.Init();
        var audit = new AuditService(db);
        var auth = new AuthService(db, audit);

        while (true)
        {
            Console.WriteLine("\n=== Файловый менеджер ===\n1. Регистрация\n2. Вход\n0. Выход");
            switch (Ask("> "))
            {
                case "1":
                    Guarded(() =>
                    {
                        var u = auth.Register(Ask("Логин: "), ReadPassword("Пароль: "));
                        Console.WriteLine($"Пользователь {u.Username} создан. Теперь войдите.");
                    });
                    break;
                case "2":
                {
                    User? user = null;
                    Guarded(() => user = auth.Login(Ask("Логин: "), ReadPassword("Пароль: ")));
                    if (user is null) { Console.WriteLine("Неверный логин или пароль."); break; }
                    Session(user, audit, baseDir);
                    break;
                }
                case "0": return;
            }
        }
    }

    private static void Session(User user, AuditService audit, string baseDir)
    {
        var files = new FileService(audit, user, baseDir);
        var json = new JsonXmlService(files);
        var zip = new ZipService(files);
        Console.WriteLine($"\nВы вошли как {user.Username}. Рабочая папка: {files.Root}");

        while (true)
        {
            Console.WriteLine("""

                --- Меню ---
                 1. Диски и файловая система     7. Прочитать JSON
                 2. Список моих файлов           8. Сохранить XML
                 3. Прочитать файл               9. Прочитать XML
                 4. Записать файл               10. Создать ZIP
                 5. Удалить файл                11. Распаковать ZIP
                 6. Сохранить JSON              12. Мой журнал действий
                 0. Выйти из аккаунта
                """);

            var choice = Ask("> ");
            if (choice == "0") return;

            Guarded(() =>
            {
                switch (choice)
                {
                    case "1": DriveService.Print(); break;
                    case "2":
                        foreach (var f in files.List()) Console.WriteLine(f);
                        break;
                    case "3": Console.WriteLine(files.ReadText(Ask("Файл: "))); break;
                    case "4":
                        files.WriteText(Ask("Файл: "), ReadMultiline(), AskYes("Перезаписать, если существует? (y/n): "));
                        Console.WriteLine("Готово.");
                        break;
                    case "5": files.Delete(Ask("Файл: ")); Console.WriteLine("Удалено."); break;
                    case "6": json.SaveJson(Ask("Файл (*.json): "), ReadNote(), AskYes("Перезаписать? (y/n): ")); Console.WriteLine("Готово."); break;
                    case "7": Console.WriteLine(json.LoadJson(Ask("Файл (*.json): "))); break;
                    case "8": json.SaveXml(Ask("Файл (*.xml): "), ReadNote(), AskYes("Перезаписать? (y/n): ")); Console.WriteLine("Готово."); break;
                    case "9": Console.WriteLine(json.LoadXml(Ask("Файл (*.xml): "))); break;
                    case "10":
                        zip.Create(Ask("Имя архива (например a.zip): "),
                                   Ask("Файлы через запятую: ").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        Console.WriteLine("Архив создан.");
                        break;
                    case "11":
                        var n = zip.Extract(Ask("Архив: "), Ask("Папка назначения (например extracted): "));
                        Console.WriteLine($"Извлечено файлов: {n}");
                        break;
                    case "12":
                        foreach (var line in audit.Recent(user.Id)) Console.WriteLine(line);
                        break;
                    default: Console.WriteLine("Неизвестный пункт."); break;
                }
            }, audit, user.Id);
        }
    }

    private static void Guarded(Action action, AuditService? audit = null, long? userId = null)
    {
        try { action(); }
        catch (SecurityViolationException ex)
        {
            Console.WriteLine($"[БЕЗОПАСНОСТЬ] {ex.Message}");
            audit?.Log(userId, "security", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or InvalidOperationException or ArgumentException or FormatException)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
        catch (Exception)
        {
            Console.WriteLine("Внутренняя ошибка. Операция отменена.");
        }
    }

    private static Note ReadNote()
    {
        var title = Ask("Заголовок: ");
        var content = Ask("Текст: ");
        if (!int.TryParse(Ask("Приоритет (1-5): "), out var prio)) prio = 0;
        return new Note(title, content, prio);
    }

    private static string ReadMultiline()
    {
        Console.WriteLine("Введите текст (пустая строка - конец):");
        var sb = new StringBuilder();
        string? line;
        while (!string.IsNullOrEmpty(line = Console.ReadLine())) sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Ask(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? "";
    }

    private static bool AskYes(string prompt) => Ask(prompt).Equals("y", StringComparison.OrdinalIgnoreCase);

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        return sb.ToString();
    }
}
