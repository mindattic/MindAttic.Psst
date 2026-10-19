namespace MindAttic.Psst.Tests.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MindAttic.Psst.Cli.Scheduling;
using NUnit.Framework;

/// <summary>
/// Psst schedules, lists and cancels through MindAttic.WatchDog's
/// <c>watchdog job</c> commands, never schtasks. These tests swap in a fake
/// WatchDog runner, so they see the exact argv Psst sends and never touch the
/// real Task Scheduler.
/// </summary>
public class WatchDogSchedulingTests
{
    private readonly List<string[]> _calls = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "psst-watchdog-tests-" + Guid.NewGuid().ToString("N"));
    private Func<IReadOnlyList<string>, Task<WatchDogClient.Result>>? _priorRunner;
    private string? _priorLocation;

    [SetUp]
    public void SetUp()
    {
        Directory.CreateDirectory(_dir);
        // The launcher's self-cancel line names watchdog.ps1; point it at a stand-in.
        var script = Path.Combine(_dir, "watchdog.ps1");
        File.WriteAllText(script, "");
        _priorLocation = Environment.GetEnvironmentVariable(WatchDogClient.LocationVariable);
        Environment.SetEnvironmentVariable(WatchDogClient.LocationVariable, script);
        _priorRunner = WatchDogClient.Runner;
        WatchDogClient.Runner = args =>
        {
            _calls.Add(args.ToArray());
            return Task.FromResult(new WatchDogClient.Result(0, "", ""));
        };
    }

    [TearDown]
    public void TearDown()
    {
        WatchDogClient.Runner = _priorRunner!;
        Environment.SetEnvironmentVariable(WatchDogClient.LocationVariable, _priorLocation);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public async Task AddAsync_RegistersAOneShotPsstJobRunningTheLauncher()
    {
        await WatchDogClient.AddAsync("abc123", new DateTime(2026, 10, 19, 9, 30, 0), @"C:\A Dir\abc123.cmd");

        Assert.That(_calls.Single(), Is.EqualTo(new[]
        {
            "job", "add", "abc123", "--owner", "Psst", "--at", "2026-10-19T09:30:00",
            "--run", "cmd.exe /c \"C:\\A Dir\\abc123.cmd\"", "--description", "Psst scheduled send",
        }));
    }

    [Test]
    public async Task CancelAsync_AcceptsThePrintedTaskNameOrTheBareId()
    {
        Assert.That(await ScheduledTaskLister.CancelAsync(@"\MindAttic\WatchDog\Psst\abc123"), Is.Null);
        Assert.That(await ScheduledTaskLister.CancelAsync("abc123"), Is.Null);

        Assert.That(_calls, Has.Count.EqualTo(2));
        Assert.That(_calls, Has.All.EqualTo(new[] { "job", "cancel", "abc123", "--owner", "Psst" }));
    }

    [Test]
    public async Task CancelAsync_RefusesTasksPsstDoesNotOwn()
    {
        var error = await ScheduledTaskLister.CancelAsync(@"\MindAttic\Automata\Tick");
        Assert.That(error, Does.Contain("refusing"));
        Assert.That(_calls, Is.Empty);
    }

    [Test]
    public async Task CancelAsync_ReportsWatchDogsReason()
    {
        WatchDogClient.Runner = _ => Task.FromResult(new WatchDogClient.Result(1, "", "watchdog: No job 'x' for owner 'Psst'\n"));
        Assert.That(await ScheduledTaskLister.CancelAsync("x"), Is.EqualTo("watchdog: No job 'x' for owner 'Psst'"));
    }

    [Test]
    public void ParseJobs_ReadsTheJsonListAndALoneObject()
    {
        var many = WatchDogClient.ParseJobs(
            """[{"name":"a","task":"\\MindAttic\\WatchDog\\Psst\\a","nextRun":"2026-10-19T09:30:00","command":"cmd.exe /c x.cmd"},{"name":"b","nextRun":null}]""");
        Assert.That(many.Select(j => j.Name), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(many[0].NextRun, Is.EqualTo(new DateTime(2026, 10, 19, 9, 30, 0)));
        Assert.That(many[0].Command, Is.EqualTo("cmd.exe /c x.cmd"));
        Assert.That(many[1].NextRun, Is.Null);

        Assert.That(WatchDogClient.ParseJobs("""{"name":"solo"}""").Single().Name, Is.EqualTo("solo"));
        Assert.That(WatchDogClient.ParseJobs("[]"), Is.Empty);
    }

    [Test]
    public void SelfCancelLine_CancelsThisJobQuietlyAndSurvivesPercents()
    {
        var line = ScheduledTaskRegistrar.SelfCancelLine(@"powershell.exe -File ""C:\50%\watchdog.ps1"" job cancel abc --owner Psst");
        Assert.That(line, Is.EqualTo(@"powershell.exe -File ""C:\50%%\watchdog.ps1"" job cancel abc --owner Psst >nul 2>nul"));
    }

    [Test]
    public void LocateScript_PrefersTheConfiguredPath()
    {
        Assert.That(WatchDogClient.LocateScript(), Is.EqualTo(Path.Combine(_dir, "watchdog.ps1")));

        Environment.SetEnvironmentVariable(WatchDogClient.LocationVariable, _dir);
        Assert.That(WatchDogClient.LocateScript(), Is.EqualTo(Path.Combine(_dir, "watchdog.ps1")));

        Environment.SetEnvironmentVariable(WatchDogClient.LocationVariable, Path.Combine(_dir, "nope"));
        Assert.Throws<FileNotFoundException>(() => WatchDogClient.LocateScript());
    }

    // ---- One-time move of reminders Psst registered itself ----

    private string WriteLegacy(string id, DateTime fireTime)
    {
        var launcher = Path.Combine(_dir, $"{id}.cmd");
        File.WriteAllText(launcher, string.Join("\r\n",
            "@echo off", "chcp 65001 >nul", "set PSST_FROM_SCHEDULE=1",
            "\"C:\\tools\\psst.exe\" sms jordan hi", "set PSST_EXIT=%ERRORLEVEL%",
            $"schtasks /Delete /TN \"MindAttic\\Psst\\MindAttic.Psst.{id}\" /F >nul 2>nul",
            $"del /Q \"{Path.Combine(_dir, id + ".json")}\" >nul 2>nul", "exit /b %PSST_EXIT%") + "\r\n");
        File.WriteAllText(Path.Combine(_dir, $"{id}.json"), JsonSerializer.Serialize(new
        {
            id,
            taskName = $@"MindAttic\Psst\MindAttic.Psst.{id}",
            fireTime = fireTime.ToString("o"),
            launcherPath = launcher,
            recipient = "jordan",
            message = "hi",
            repeat = 1,
            intervalSeconds = 0,
        }));
        return launcher;
    }

    [Test]
    public async Task Migrate_RecreatesDueRemindersThroughWatchDogAndRetiresTheOldTask()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0);
        var due = WriteLegacy("due111", now.AddHours(3));
        WriteLegacy("past22", now.AddHours(-3));

        var failures = await LegacyTaskMigration.MigrateAsync(_dir, now);

        Assert.That(failures, Is.Empty);
        var add = _calls.Single();
        Assert.That(add.Take(3), Is.EqualTo(new[] { "job", "add", "due111" }));
        Assert.That(add, Does.Contain("--replaces"));
        Assert.That(add[Array.IndexOf(add, "--replaces") + 1], Is.EqualTo(@"\MindAttic\Psst\MindAttic.Psst.due111"));
        Assert.That(add[Array.IndexOf(add, "--at") + 1], Is.EqualTo(now.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss")));

        var launcher = File.ReadAllText(due);
        Assert.That(launcher, Does.Not.Contain("schtasks"));
        Assert.That(launcher, Does.Contain("job cancel due111 --owner Psst >nul 2>nul"));
        Assert.That(launcher, Does.Contain("\"C:\\tools\\psst.exe\" sms jordan hi"));

        using var sidecar = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "due111.json")));
        Assert.That(sidecar.RootElement.GetProperty("taskName").GetString(), Is.EqualTo(@"\MindAttic\WatchDog\Psst\due111"));
        Assert.That(sidecar.RootElement.GetProperty("message").GetString(), Is.EqualTo("hi"));

        // Already moved: a second pass has nothing left to do.
        _calls.Clear();
        Assert.That(await LegacyTaskMigration.MigrateAsync(_dir, now), Is.Empty);
        Assert.That(_calls, Is.Empty);
    }

    [Test]
    public async Task RunOnce_WritesItsMarkerOnlyWhenEveryReminderMoved()
    {
        WriteLegacy("due333", DateTime.Now.AddHours(3));
        WatchDogClient.Runner = args =>
        {
            _calls.Add(args.ToArray());
            return Task.FromResult(new WatchDogClient.Result(1, "", "watchdog: boom"));
        };

        await LegacyTaskMigration.RunOnceAsync(_dir);
        Assert.That(File.Exists(Path.Combine(_dir, ".moved-to-watchdog")), Is.False);

        WatchDogClient.Runner = args =>
        {
            _calls.Add(args.ToArray());
            return Task.FromResult(new WatchDogClient.Result(0, "", ""));
        };
        await LegacyTaskMigration.RunOnceAsync(_dir);
        Assert.That(File.Exists(Path.Combine(_dir, ".moved-to-watchdog")), Is.True);

        _calls.Clear();
        await LegacyTaskMigration.RunOnceAsync(_dir);
        Assert.That(_calls, Is.Empty);
    }
}
