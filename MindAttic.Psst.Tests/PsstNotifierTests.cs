namespace MindAttic.Psst.Tests;

using MindAttic.Psst;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
using NUnit.Framework;

public class PsstNotifierTests
{
    private static Func<CancellationToken, Task<PsstPlayResult>> SoundOk(string transport = "MP3 (NAudio)") =>
        _ => Task.FromResult(PsstPlayResult.Ok(transport));

    private static Func<CancellationToken, Task<PsstPlayResult>> SoundFail(string error = "boom") =>
        _ => Task.FromResult(PsstPlayResult.Fail(error));

    [Test]
    public async Task NotifyAsync_NoTransports_ReturnsEmptyAttempts()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundFail());

        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.That(result.SoundPlayed, Is.False);
        Assert.That(result.Sound, Is.Null);
        Assert.That(result.SmsAttempts, Is.Empty);
        Assert.That(result.AnySmsSent, Is.False);
        Assert.That(result.FirstSuccess, Is.Null);
    }

    [Test]
    public async Task NotifyAsync_Silent_DoesNotInvokeSoundPlayer()
    {
        var calls = 0;
        var notifier = new PsstNotifier(
            new[] { new StubClient("A", success: true) },
            _ => { calls++; return Task.FromResult(PsstPlayResult.Ok("MP3")); });

        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.That(calls, Is.EqualTo(0));
        Assert.That(result.SoundPlayed, Is.False);
        Assert.That(result.Sound, Is.Null);
    }

    [Test]
    public async Task NotifyAsync_NotSilent_InvokesSoundPlayerAndReportsResult()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundOk("MP3 (NAudio)"));

        var result = await notifier.NotifyAsync("hi");

        Assert.That(result.SoundPlayed, Is.True);
        Assert.That(result.Sound, Is.Not.Null);
        Assert.That(result.Sound!.Transport, Is.EqualTo("MP3 (NAudio)"));
    }

    [Test]
    public async Task NotifyAsync_SoundPlayerFails_PropagatesFalseAndKeepsError()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundFail("no audio device"));

        var result = await notifier.NotifyAsync("hi");

        Assert.That(result.SoundPlayed, Is.False);
        Assert.That(result.Sound, Is.Not.Null);
        Assert.That(result.Sound!.Error, Is.EqualTo("no audio device"));
    }

    [Test]
    public async Task NotifyAsync_FirstTransportSucceeds_DoesNotCallSecond()
    {
        var first = new StubClient("Primary", success: true);
        var second = new StubClient("Secondary", success: true);

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.That(first.CallCount, Is.EqualTo(1));
        Assert.That(second.CallCount, Is.EqualTo(0));
        Assert.That(result.SmsAttempts, Has.Exactly(1).Items);
        Assert.That(result.AnySmsSent, Is.True);
        Assert.That(result.FirstSuccess!.Transport, Is.EqualTo("Primary"));
    }

    [Test]
    public async Task NotifyAsync_FirstTransportFails_FallsBackToSecond()
    {
        var first = new StubClient("Primary", success: false, detail: "401");
        var second = new StubClient("Secondary", success: true);

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.That(first.CallCount, Is.EqualTo(1));
        Assert.That(second.CallCount, Is.EqualTo(1));
        Assert.That(result.SmsAttempts.Count, Is.EqualTo(2));
        Assert.That(result.SmsAttempts[0].Success, Is.False);
        Assert.That(result.SmsAttempts[1].Success, Is.True);
        Assert.That(result.FirstSuccess!.Transport, Is.EqualTo("Secondary"));
    }

    [Test]
    public async Task NotifyAsync_AllTransportsFail_ReportsAllAttempts()
    {
        var first = new StubClient("Primary", success: false, detail: "401");
        var second = new StubClient("Secondary", success: false, detail: "smtp boom");

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.That(result.SmsAttempts.Count, Is.EqualTo(2));
        Assert.That(result.AnySmsSent, Is.False);
        Assert.That(result.FirstSuccess, Is.Null);
    }

    [Test]
    public async Task NotifyAsync_PassesMessageToTransport()
    {
        var client = new StubClient("X", success: true);
        var notifier = new PsstNotifier(new[] { client }, SoundFail());

        await notifier.NotifyAsync("hello world", silent: true);

        Assert.That(client.LastMessage, Is.EqualTo("hello world"));
    }

    [Test]
    public async Task NotifyAsync_PropagatesCancellationToken()
    {
        var client = new StubClient("X", success: true);
        var notifier = new PsstNotifier(new[] { client }, SoundFail());
        using var cts = new CancellationTokenSource();

        await notifier.NotifyAsync("x", silent: true, cancellationToken: cts.Token);

        Assert.That(client.LastToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public async Task NotifyAsync_RunsSoundAndSmsInParallel()
    {
        // Sound holds for ~100ms; SMS also holds for ~100ms. If they were
        // serialized the total would be ~200ms — assert it stays well under
        // 180ms to confirm concurrent execution.
        var soundGate = new TaskCompletionSource();
        Task<PsstPlayResult> Play(CancellationToken ct) => DelayedSound(soundGate, ct);

        var smsClient = new DelayedClient(TimeSpan.FromMilliseconds(100));
        var notifier = new PsstNotifier(new[] { smsClient }, Play);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = notifier.NotifyAsync("x");
        // Let both start; release sound after a beat.
        await Task.Delay(20);
        soundGate.SetResult();
        await task;
        sw.Stop();

        Assert.That(sw.Elapsed < TimeSpan.FromMilliseconds(180), Is.True, $"expected concurrent execution but took {sw.ElapsedMilliseconds}ms");

        static async Task<PsstPlayResult> DelayedSound(TaskCompletionSource gate, CancellationToken ct)
        {
            await gate.Task.WaitAsync(ct);
            return PsstPlayResult.Ok("MP3");
        }
    }

    private sealed class StubClient : ISmsClient
    {
        private readonly bool _success;
        private readonly string? _detail;
        public string TransportName { get; }
        public int CallCount { get; private set; }
        public string? LastMessage { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public StubClient(string name, bool success, string? detail = null)
        {
            TransportName = name;
            _success = success;
            _detail = detail;
        }

        public Task<SmsResult> SendAsync(string message, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastMessage = message;
            LastToken = cancellationToken;
            return Task.FromResult(new SmsResult(_success, TransportName, _detail));
        }
    }

    private sealed class DelayedClient : ISmsClient
    {
        private readonly TimeSpan _delay;
        public string TransportName => "Delayed";
        public DelayedClient(TimeSpan delay) { _delay = delay; }
        public async Task<SmsResult> SendAsync(string message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return new SmsResult(true, TransportName, "ok");
        }
    }
}
