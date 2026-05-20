namespace MindAttic.Psst.Tests;

using MindAttic.Psst;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
using Xunit;

public class NotifyResultTests
{
    [Fact]
    public void AnySmsSent_FalseWhenEmpty()
    {
        var r = new NotifyResult(null, Array.Empty<SmsResult>());
        Assert.False(r.AnySmsSent);
        Assert.Null(r.FirstSuccess);
        Assert.False(r.SoundPlayed);
    }

    [Fact]
    public void AnySmsSent_FalseWhenAllFailed()
    {
        var r = new NotifyResult(PsstPlayResult.Ok("MP3"), new[]
        {
            new SmsResult(false, "Twilio", "401"),
            new SmsResult(false, "Email-to-SMS", "smtp boom"),
        });
        Assert.False(r.AnySmsSent);
        Assert.Null(r.FirstSuccess);
        Assert.True(r.SoundPlayed);
    }

    [Fact]
    public void FirstSuccess_ReturnsFirstSuccessfulAttempt()
    {
        var second = new SmsResult(true, "Email-to-SMS", "sent");
        var r = new NotifyResult(PsstPlayResult.Ok("MP3"), new[]
        {
            new SmsResult(false, "Twilio", "401"),
            second,
        });
        Assert.True(r.AnySmsSent);
        Assert.Same(second, r.FirstSuccess);
    }

    [Fact]
    public void SoundPlayed_FalseWhenSoundFailed()
    {
        var r = new NotifyResult(PsstPlayResult.Fail("no audio"), Array.Empty<SmsResult>());
        Assert.False(r.SoundPlayed);
    }
}
