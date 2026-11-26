using System.IO.Compression;
using FileManager.Models;
using FileManager.Services;

namespace FileManager.Tests;

public class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "fm-root");

    [Fact]
    public void Resolve_NormalPath_StaysInsideRoot()
    {
        var p = PathGuard.Resolve(Root, "docs/a.txt");
        Assert.StartsWith(Path.GetFullPath(Root), p);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/etc/passwd")]
    public void Resolve_Escape_Throws(string rel) =>
        Assert.Throws<SecurityViolationException>(() => PathGuard.Resolve(Root, rel));

    [Fact]
    public void Resolve_NullByte_Throws() =>
        Assert.Throws<SecurityViolationException>(() => PathGuard.Resolve(Root, "a\0b.txt"));

    [Fact]
    public void Resolve_Empty_Throws() =>
        Assert.Throws<ArgumentException>(() => PathGuard.Resolve(Root, "  "));
}

public class AuthServiceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Hash_Then_Verify_Works()
    {
        var h = AuthService.Hash("correct-horse-1");
        Assert.True(AuthService.Verify("correct-horse-1", h));
        Assert.False(AuthService.Verify("wrong-password", h));
    }

    [Fact]
    public void Hash_UsesRandomSalt() =>
        Assert.NotEqual(AuthService.Hash("same-password-1"), AuthService.Hash("same-password-1"));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    public void Verify_MalformedHash_ReturnsFalse(string stored) =>
        Assert.False(AuthService.Verify("whatever", stored));

    [Theory]
    [InlineData("ab")]
    [InlineData("bad name!")]
    [InlineData("../root")]
    public void Register_InvalidUsername_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => _env.Auth.Register(name, "Passw0rd!123"));

    [Fact]
    public void Register_ShortPassword_Throws() =>
        Assert.Throws<ArgumentException>(() => _env.Auth.Register("validuser", "short"));

    [Fact]
    public void Register_Duplicate_IsCaseInsensitive()
    {
        _env.Auth.Register("alice", "Passw0rd!123");
        Assert.Throws<InvalidOperationException>(() => _env.Auth.Register("ALICE", "Passw0rd!123"));
    }

    [Fact]
    public void Login_CorrectPassword_ReturnsUser()
    {
        _env.Auth.Register("bob", "Passw0rd!123");
        var u = _env.Auth.Login("bob", "Passw0rd!123");
        Assert.NotNull(u);
        Assert.Equal("bob", u!.Username);
    }

    [Fact]
    public void Login_WrongPassword_ReturnsNull()
    {
        _env.Auth.Register("carol", "Passw0rd!123");
        Assert.Null(_env.Auth.Login("carol", "not-the-password"));
    }

    [Fact]
    public void Login_UnknownUser_ReturnsNull() =>
        Assert.Null(_env.Auth.Login("nobody", "Passw0rd!123"));
}

public class FileServiceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Write_Then_Read_ReturnsSameText()
    {
        var f = _env.NewFiles();
        f.WriteText("a.txt", "hello", false);
        Assert.Equal("hello", f.ReadText("a.txt"));
    }

    [Fact]
    public void Write_Existing_WithoutOverwrite_Throws()
    {
        var f = _env.NewFiles();
        f.WriteText("a.txt", "one", false);
        Assert.Throws<InvalidOperationException>(() => f.WriteText("a.txt", "two", false));
    }

    [Fact]
    public void Write_Existing_WithOverwrite_Replaces()
    {
        var f = _env.NewFiles();
        f.WriteText("a.txt", "one", false);
        f.WriteText("a.txt", "two", true);
        Assert.Equal("two", f.ReadText("a.txt"));
    }

    [Fact]
    public void Delete_RemovesFile_AndMissingFileThrows()
    {
        var f = _env.NewFiles();
        f.WriteText("a.txt", "x", false);
        f.Delete("a.txt");
        Assert.False(File.Exists(Path.Combine(f.Root, "a.txt")));
        Assert.Throws<FileNotFoundException>(() => f.Delete("a.txt"));
    }

    [Fact]
    public void Read_PathTraversal_Throws()
    {
        var f = _env.NewFiles();
        Assert.Throws<SecurityViolationException>(() => f.ReadText("../../etc/passwd"));
    }

    [Fact]
    public void Write_TooLarge_Throws()
    {
        var f = _env.NewFiles();
        var big = new string('a', (int)FileService.MaxFileBytes + 1);
        Assert.Throws<InvalidOperationException>(() => f.WriteText("big.txt", big, false));
    }

    [Fact]
    public void List_ReturnsRelativePaths()
    {
        var f = _env.NewFiles();
        f.WriteText("x/b.txt", "b", false);
        f.WriteText("a.txt", "a", false);
        Assert.Equal(new[] { "a.txt", "x/b.txt" }, f.List().ToArray());
    }

    [Fact]
    public void Operations_AreWrittenToAuditLog()
    {
        var user = _env.Auth.Register("auditor", "Passw0rd!123");
        var f = new FileService(_env.Audit, user, _env.Dir);
        f.WriteText("a.txt", "x", false);
        f.ReadText("a.txt");
        var log = _env.Audit.Recent(user.Id);
        Assert.Contains(log, l => l.Contains("create"));
        Assert.Contains(log, l => l.Contains("read"));
    }
}

public class JsonXmlServiceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Json_RoundTrip()
    {
        var svc = new JsonXmlService(_env.NewFiles());
        var note = new Note("Title", "Content", 3);
        svc.SaveJson("n.json", note, false);
        Assert.Equal(note, svc.LoadJson("n.json"));
    }

    [Fact]
    public void Xml_RoundTrip()
    {
        var svc = new JsonXmlService(_env.NewFiles());
        var note = new Note("Title", "Content", 4);
        svc.SaveXml("n.xml", note, false);
        Assert.Equal(note, svc.LoadXml("n.xml"));
    }

    [Fact]
    public void Save_InvalidPriority_Throws()
    {
        var svc = new JsonXmlService(_env.NewFiles());
        Assert.Throws<InvalidDataException>(() => svc.SaveJson("b.json", new Note("T", "C", 9), false));
    }

    [Fact]
    public void Json_UnknownField_IsRejected()
    {
        var f = _env.NewFiles();
        var svc = new JsonXmlService(f);
        f.WriteText("x.json", "{\"Title\":\"T\",\"Content\":\"C\",\"Priority\":1,\"Admin\":true}", false);
        Assert.Throws<InvalidDataException>(() => svc.LoadJson("x.json"));
    }

    [Fact]
    public void Xml_ExternalEntity_IsBlocked()
    {
        var f = _env.NewFiles();
        var svc = new JsonXmlService(f);
        var xxe = """<?xml version="1.0"?><!DOCTYPE note [<!ENTITY x SYSTEM "file:///etc/passwd">]><note><title>&x;</title><content>c</content><priority>1</priority></note>""";
        f.WriteText("xxe.xml", xxe, false);
        Assert.Throws<InvalidDataException>(() => svc.LoadXml("xxe.xml"));
    }
}

public class ZipServiceTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    [Fact]
    public void Create_Then_Extract_RoundTrip()
    {
        var f = _env.NewFiles();
        var zip = new ZipService(f);
        f.WriteText("a.txt", "aaa", false);
        f.WriteText("b.txt", "bbb", false);
        zip.Create("pack.zip", new[] { "a.txt", "b.txt" });
        Assert.Equal(2, zip.Extract("pack.zip", "out"));
        Assert.Equal("aaa", f.ReadText("out/a.txt"));
    }

    [Fact]
    public void Extract_ZipSlip_IsBlocked()
    {
        var f = _env.NewFiles();
        var zip = new ZipService(f);
        var path = Path.Combine(f.Root, "evil.zip");
        using (var fs = File.Create(path))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry("../evil.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("pwned");
        }
        Assert.Throws<SecurityViolationException>(() => zip.Extract("evil.zip", "out"));
        Assert.False(File.Exists(Path.Combine(f.Root, "evil.txt")));
    }

    [Fact]
    public void Extract_ZipBomb_IsBlocked()
    {
        var f = _env.NewFiles();
        var zip = new ZipService(f);
        var path = Path.Combine(f.Root, "bomb.zip");
        using (var fs = File.Create(path))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry("bomb.bin", CompressionLevel.SmallestSize);
            using var s = e.Open();
            s.Write(new byte[5 * 1024 * 1024]);
        }
        Assert.Throws<SecurityViolationException>(() => zip.Extract("bomb.zip", "out"));
    }
}
