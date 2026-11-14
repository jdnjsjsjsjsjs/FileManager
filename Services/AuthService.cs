using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FileManager.Data;
using FileManager.Models;
using Microsoft.Data.Sqlite;

namespace FileManager.Services;

public sealed class AuthService(Database db, AuditService audit)
{
    private const int Iterations = 210_000;
    private const string Algo = "pbkdf2-sha256";
    private static readonly Regex NameRx = new("^[A-Za-z0-9_]{3,32}$", RegexOptions.Compiled);
    private static readonly string DummyHash = Hash("dummy-password");

    public User Register(string username, string password)
    {
        if (!NameRx.IsMatch(username))
            throw new ArgumentException("Логин: 3-32 символа, только латиница, цифры и _");
        if (password.Length is < 8 or > 128)
            throw new ArgumentException("Пароль: от 8 до 128 символов");

        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Users (username, password_hash) VALUES ($u, $h) RETURNING id;";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$h", Hash(password));

        long id;
        try { id = Convert.ToInt64(cmd.ExecuteScalar()); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Такой логин уже занят");
        }

        audit.Log(id, "register");
        return new User(id, username);
    }

    public User? Login(string username, string password)
    {
        if (username.Length > 64 || password.Length > 128) return null;

        long id = 0; string? name = null, hash = null;
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, username, password_hash FROM Users WHERE username = $u;";
            cmd.Parameters.AddWithValue("$u", username);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { id = r.GetInt64(0); name = r.GetString(1); hash = r.GetString(2); }
        }

        var ok = Verify(password, hash ?? DummyHash) && hash is not null;

        if (!ok)
        {
            var safeName = new string(username.Where(ch => !char.IsControl(ch)).Take(32).ToArray());
            audit.Log(hash is null ? null : id, "login_failed", $"login={safeName}");
            return null;
        }

        audit.Log(id, "login");
        return new User(id, name!);
    }

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Algo}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != Algo || !int.TryParse(p[1], out var iter) || iter < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(p[2]);
            var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
