namespace MindAttic.Psst.Tests.Cli;

using MindAttic.Psst.Cli.Triggers;
using MindAttic.Psst.Sms;
using NUnit.Framework;

/// <summary>The trigger catalog and a watch's decision to announce (M.PSST-T41).</summary>
public class WatchTests
{
    private static readonly DateTime Noon = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Local);

    private static TriggerDef Def(TriggerKind kind) =>
        new("T", [], "", 0, 0, kind, "", (_, _) => TriggerProbe.Is(true));

    [TestCase("NoActiveClisDetected")]
    [TestCase("noactiveclisdetected")]
    [TestCase("no-active-clis-detected")]
    [TestCase("NoActiveClis")]
    public void Find_MatchesWhateverTheCaseOrPunctuation(string name) =>
        Assert.That(TriggerCatalog.Find(name)?.Name, Is.EqualTo("NoActiveClisDetected"));

    [Test]
    public void Find_UnknownNameIsNull() => Assert.That(TriggerCatalog.Find("Bogus"), Is.Null);

    [Test]
    public void Catalog_NamesAndAliasesAreUnique()
    {
        var keys = TriggerCatalog.All.SelectMany(t => t.Aliases.Append(t.Name)).Select(TriggerCatalog.Key).ToList();
        Assert.That(keys, Is.Unique);
    }

    [Test]
    public void Parse_SplitsArgumentsAndKeepsQuotedCommas()
    {
        var (name, args, error) = TriggerCatalog.Parse("DiskFreeBelow(C, 20)");
        Assert.That(error, Is.Null);
        Assert.That(name, Is.EqualTo("DiskFreeBelow"));
        Assert.That(args, Is.EqualTo(new[] { "C", "20" }));

        (_, args, _) = TriggerCatalog.Parse("CommandSucceeds(\"a, b\")");
        Assert.That(args, Is.EqualTo(new[] { "a, b" }));

        (name, args, _) = TriggerCatalog.Parse("OrchestratorRedeployed");
        Assert.That(name, Is.EqualTo("OrchestratorRedeployed"));
        Assert.That(args, Is.Empty);

        Assert.That(TriggerCatalog.Parse("FileExists(C:\\x").Error, Is.Not.Null);
    }

    [Test]
    public void Condition_AnnouncesOnceUntilItHasBeenFalseAgain()
    {
        var def = Def(TriggerKind.Condition);
        var watch = new Watch { Recurring = true, Created = Noon };

        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(false), Noon), Is.False);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(true), Noon), Is.True);
        watch.Fired(def, TriggerProbe.Is(true), Noon);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(true), Noon.AddMinutes(1)), Is.False, "still true: no second announcement");
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(false), Noon.AddMinutes(2)), Is.False);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(true), Noon.AddMinutes(3)), Is.True, "true again after being false");
    }

    [Test]
    public void Condition_UnsentAnnouncementIsTriedAgain()
    {
        var def = Def(TriggerKind.Condition);
        var watch = new Watch { Created = Noon };
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(true), Noon), Is.True);
        // Fired() was never called (the send failed), so the next look asks again.
        Assert.That(watch.ShouldFire(def, TriggerProbe.Is(true), Noon.AddMinutes(1)), Is.True);
    }

    [Test]
    public void Change_FirstValueIsTheBaselineAndADifferentOneAnnounces()
    {
        var def = Def(TriggerKind.Change);
        var watch = new Watch { Created = Noon };

        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("a"), Noon), Is.False);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("a"), Noon), Is.False);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("b"), Noon), Is.True);
        watch.Fired(def, TriggerProbe.Value("b"), Noon);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("b"), Noon), Is.False);
    }

    [Test]
    public void FailedLook_NeverAnnouncesAndNeverReArms()
    {
        var def = Def(TriggerKind.Condition);
        var watch = new Watch { Created = Noon, Armed = false };
        Assert.That(watch.ShouldFire(def, TriggerProbe.Failed("tool missing"), Noon), Is.False);
        Assert.That(watch.Armed, Is.False);
    }

    [Test]
    public void Cooldown_KeepsARecurringWatchQuiet()
    {
        var def = Def(TriggerKind.Change);
        var watch = new Watch { Recurring = true, CooldownSeconds = 600, Created = Noon, Fingerprint = "a", LastFired = Noon };
        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("b"), Noon.AddMinutes(5)), Is.False);
        Assert.That(watch.ShouldFire(def, TriggerProbe.Value("b"), Noon.AddMinutes(11)), Is.True);
    }

    [Test]
    public void Clock_AlwaysKeepsGoing_AndAOneTimeConditionDoesNot()
    {
        Assert.That(new Watch().KeepsGoing(Def(TriggerKind.Pulse)), Is.True);
        Assert.That(new Watch().KeepsGoing(Def(TriggerKind.Condition)), Is.False);
        Assert.That(new Watch { Recurring = true }.KeepsGoing(Def(TriggerKind.Condition)), Is.True);
    }

    [Test]
    public void Daily_IsDueOncePerDayAfterItsTime()
    {
        var daily = TriggerCatalog.Find("Daily")!;
        var created = Noon.Date.AddHours(8);
        string[] at9 = ["9am"];
        Assert.That(daily.Probe(at9, new TriggerContext(Noon.Date.AddHours(8.5), created, null)).Holds, Is.False);
        Assert.That(daily.Probe(at9, new TriggerContext(Noon.Date.AddHours(9).AddMinutes(1), created, null)).Holds, Is.True);
        Assert.That(daily.Probe(at9, new TriggerContext(Noon, created, Noon.Date.AddHours(9).AddMinutes(1))).Holds, Is.False, "already announced today");
        Assert.That(daily.Probe(at9, new TriggerContext(Noon.AddDays(1), created, Noon.Date.AddHours(9).AddMinutes(1))).Holds, Is.True, "due again the next day");
    }

    [Test]
    public void Announcement_UsesTheOwnersWordsElseTheTriggersSentence()
    {
        var def = TriggerCatalog.Find("OrchestratorRedeployed")!;
        Assert.That(new Watch { Trigger = def.Name }.Announcement(def, TriggerProbe.Value("1", "built 2026-11-18 12:19")),
            Is.EqualTo("MindAttic.Orchestrator was redeployed. (built 2026-11-18 12:19)"));
        Assert.That(new Watch { Trigger = def.Name, Message = "new dashboard" }.Announcement(def, TriggerProbe.Value("1")), Is.EqualTo("new dashboard"));
    }

    [Test]
    public void InboxSubject_IsTheFirstLineShortened()
    {
        Assert.That(EmailSmsClient.SubjectFor("psst: build — OK in 12m04s"), Is.EqualTo("Psst: build — OK in 12m04s"));
        Assert.That(EmailSmsClient.SubjectFor("first\nsecond"), Is.EqualTo("Psst: first"));
        Assert.That(EmailSmsClient.SubjectFor(new string('x', 200)).Length, Is.EqualTo(EmailSmsClient.SubjectMaxChars));
    }
}
