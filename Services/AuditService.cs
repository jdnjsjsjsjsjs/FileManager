using FileManager.Data;
using Microsoft.Data.Sqlite;

namespace FileManager.Services;

public sealed class AuditService(Database db)
{
    public void Log(long? userId, string type, string? details = null)
    {
        using var c = db.Open();
        Insert(c, null, userId, type, null, details);
    }

    public void LogFile(long userId, string location, string filename, long size,
                        string type, string? details = null)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        long fileId;

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Files (filename, size, location, owner_id, is_deleted)
                VALUES ($name, $size, $loc, $owner, $del)
                ON CONFLICT (owner_id, location) DO UPDATE SET
                    size       = CASE WHEN excluded.is_deleted = 1 THEN Files.size ELSE excluded.size END,
                    created_at = CASE WHEN Files.is_deleted = 1 AND excluded.is_deleted = 0
                                      THEN CURRENT_TIMESTAMP ELSE Files.created_at END,
                    is_deleted = excluded.is_deleted
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$name", filename);
            cmd.Parameters.AddWithValue("$size", size);
            cmd.Parameters.AddWithValue("$loc", location);
            cmd.Parameters.AddWithValue("$owner", userId);
            cmd.Parameters.AddWithValue("$del", type == "delete" ? 1 : 0);
            fileId = Convert.ToInt64(cmd.ExecuteScalar());
        }

        Insert(c, tx, userId, type, fileId, details);
        tx.Commit();
    }

    public List<string> Recent(long userId, int count = 20)
    {
        var result = new List<string>();
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT o.timestamp, o.operation_type, f.location, o.details
            FROM Operations o LEFT JOIN Files f ON f.id = o.file_id
            WHERE o.user_id = $u
            ORDER BY o.id DESC LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$n", count);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var file = r.IsDBNull(2) ? "-" : r.GetString(2);
            var det = r.IsDBNull(3) ? "" : r.GetString(3);
            result.Add($"{r.GetString(0)}  {r.GetString(1),-12} {file}  {det}".TrimEnd());
        }
        return result;
    }

    private static void Insert(SqliteConnection c, SqliteTransaction? tx, long? userId,
                               string type, long? fileId, string? details)
    {
        if (details is { Length: > 500 }) details = details[..500];

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO Operations (operation_type, file_id, user_id, details)
            VALUES ($t, $f, $u, $d);
            """;
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$f", (object?)fileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)details ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
