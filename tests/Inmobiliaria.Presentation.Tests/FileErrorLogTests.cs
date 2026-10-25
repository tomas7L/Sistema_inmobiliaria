using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// Live smoke H.5 (2026-10-24): the file the agency's PC keeps of every unexpected failure. It carries the
/// exception, never a password, and a failure to write it never reaches the operator.
/// </summary>
public sealed class FileErrorLogTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 21, 1, 30, TimeSpan.FromHours(-3));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "inmobiliaria-errorlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ARecord_IsWrittenToTheDaysFile_WithTimeContextTypeMessageAndStack()
    {
        var log = new FileErrorLog(_folder, () => At);

        log.Record("Read", Thrown(new TimeoutException("The pooler did not answer.")));

        var text = File.ReadAllText(Path.Combine(_folder, "errores-2026-10-24.log"));
        Assert.Contains("2026-10-24 21:01:30 -03:00", text);
        Assert.Contains("[Read]", text);
        Assert.Contains("System.TimeoutException: The pooler did not answer.", text);
        Assert.Contains(nameof(Thrown), text);
    }

    [Fact]
    public void TwoRecords_AreBothKept_InOrder()
    {
        var log = new FileErrorLog(_folder, () => At);

        log.Record("Read", new InvalidOperationException("first"));
        log.Record("Commit", new InvalidOperationException("second"));

        var text = File.ReadAllText(Path.Combine(_folder, "errores-2026-10-24.log"));
        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void APasswordInTheExceptionText_IsNeverWritten()
    {
        var log = new FileErrorLog(_folder, () => At);

        log.Record("Read", new InvalidOperationException(
            "Host=db.internal;Username=nicolas;Password=hunter2;SSL Mode=Require timed out",
            new InvalidOperationException("password = 'other secret' rejected")));

        var text = File.ReadAllText(Path.Combine(_folder, "errores-2026-10-24.log"));
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("other secret", text);
        Assert.Contains("Password=***", text);
        Assert.Contains("Host=db.internal", text);
    }

    [Fact]
    public void AFolderThatCannotBeWritten_IsSwallowed()
    {
        // A file where the folder should be: creating the folder fails.
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        File.WriteAllText(_folder, "not a folder");
        try
        {
            var log = new FileErrorLog(_folder, () => At);

            var record = () => log.Record("Read", new InvalidOperationException("lost"));

            var thrown = Record.Exception(record);
            Assert.Null(thrown);
        }
        finally
        {
            File.Delete(_folder);
        }
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
