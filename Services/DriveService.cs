namespace FileManager.Services;

public static class DriveService
{
    public static void Print()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                Console.WriteLine($"Диск: {d.Name}   Тип: {d.DriveType}");
                if (!d.IsReady) { Console.WriteLine("  не готов\n"); continue; }
                Console.WriteLine($"  ФС: {d.DriveFormat}   Метка: {(string.IsNullOrEmpty(d.VolumeLabel) ? "-" : d.VolumeLabel)}");
                Console.WriteLine($"  Объём: {Gb(d.TotalSize)}   Свободно: {Gb(d.TotalFreeSpace)}   Доступно: {Gb(d.AvailableFreeSpace)}\n");
            }
            catch (IOException) { Console.WriteLine("  не удалось получить сведения\n"); }
        }
    }

    private static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:F2} ГБ";
}
