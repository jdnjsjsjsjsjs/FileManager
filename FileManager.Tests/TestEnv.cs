using FileManager.Data;
using FileManager.Services;
using Microsoft.Data.Sqlite;

namespace FileManager.Tests;

public sealed class TestEnv : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "fm-tests-" + Guid.NewGuid().ToString("N"));
    public Database Db { get; }
    public AuditService Audit { get; }
    public AuthService Auth { get; }

    public TestEnv()
    {
        Directory.CreateDirectory(Dir);
        Db = new Database(Path.Combine(Dir, "test.db"));
        Db.Init();
        Audit = new AuditService(Db);
        Auth = new AuthService(Db, Audit);
    }

    public FileService NewFiles(string username = "tester")
    {
        var user = Auth.Register(username, "Passw0rd!123");
        return new FileService(Audit, user, Dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch { /* best effort */ }
    }
}
