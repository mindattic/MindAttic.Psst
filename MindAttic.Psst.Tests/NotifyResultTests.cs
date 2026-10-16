namespace MindAttic.Psst.Tests;

using MindAttic.Psst;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
using NUnit.Framework;

public class NotifyResultTests
{
    [Test]
    public void AnySmsSent_FalseWhenEmpty()
    {
        var r = new NotifyResult(null, Array.Empty<SmsResult>());
        Assert.That(r.AnySmsSent, Is.False);
        Assert.That(r.FirstSuccess, Is.Null);
        Assert.That(r.SoundPlayed, Is.False);
    }

    [Test]
    public void AnySmsSent_FalseWhenAllFailed()
    {
        var r = new NotifyResult(PsstPlayResult.Ok("MP3"), new[]
        {
            new SmsResult(false, "Transport-A", "401"),
            new SmsResult(false, "Email-to-SMS", "smtp boom"),
        });
        Assert.That(r.AnySmsSent, Is.False);
        Assert.That(r.FirstSuccess, Is.Null);
        Assert.That(r.SoundPlayed, Is.True);
    }

    [Test]
    public void FirstSuccess_ReturnsFirstSuccessfulAttempt()
    {
        var second = new SmsResult(true, "Email-to-SMS", "sent");
        var r = new NotifyResult(PsstPlayResult.Ok("MP3"), new[]
        {
            new SmsResult(false, "Transport-A", "401"),
            second,
        });
        Assert.That(r.AnySmsSent, Is.True);
        Assert.That(r.FirstSuccess, Is.SameAs(second));
    }

    [Test]
    public void SoundPlayed_FalseWhenSoundFailed()
    {
        var r = new NotifyResult(PsstPlayResult.Fail("no audio"), Array.Empty<SmsResult>());
        Assert.That(r.SoundPlayed, Is.False);
    }
}
