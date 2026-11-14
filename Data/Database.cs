using Microsoft.Data.Sqlite;
namespace FileManager.Data;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Users (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
            password_hash TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Files (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            filename   TEXT NOT NULL,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            size       INTEGER NOT NULL DEFAULT 0,
            location   TEXT NOT NULL,
            owner_id   INTEGER NOT NULL REFERENCES Users(id),
            is_deleted INTEGER NOT NULL DEFAULT 0,
            UNIQUE (owner_id, location)
        );

        CREATE TABLE IF NOT EXISTS Operations (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            operation_type TEXT NOT NULL CHECK (operation_type IN
                ('create','modify','delete','read','register','login','login_failed','security')),
            file_id        INTEGER REFERENCES Files(id),
            user_id        INTEGER REFERENCES Users(id),
            details        TEXT
        );
        """;
}
