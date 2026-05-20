namespace MindAttic.Psst.Tests;

using MindAttic.Psst;
using MindAttic.Psst.Sms;
using MindAttic.Psst.Sound;
using Xunit;

public class PsstNotifierTests
{
    private static Func<CancellationToken, Task<PsstPlayResult>> SoundOk(string transport = "MP3 (NAudio)") =>
        _ => Task.FromResult(PsstPlayResult.Ok(transport));

    private static Func<CancellationToken, Task<PsstPlayResult>> SoundFail(string error = "boom") =>
        _ => Task.FromResult(PsstPlayResult.Fail(error));

    [Fact]
    public async Task NotifyAsync_NoTransports_ReturnsEmptyAttempts()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundFail());

        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.False(result.SoundPlayed);
        Assert.Null(result.Sound);
        Assert.Empty(result.SmsAttempts);
        Assert.False(result.AnySmsSent);
        Assert.Null(result.FirstSuccess);
    }

    [Fact]
    public async Task NotifyAsync_Silent_DoesNotInvokeSoundPlayer()
    {
        var calls = 0;
        var notifier = new PsstNotifier(
            new[] { new StubClient("A", success: true) },
            _ => { calls++; return Task.FromResult(PsstPlayResult.Ok("MP3")); });

        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.Equal(0, calls);
        Assert.False(result.SoundPlayed);
        Assert.Null(result.Sound);
    }

    [Fact]
    public async Task NotifyAsync_NotSilent_InvokesSoundPlayerAndReportsResult()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundOk("MP3 (NAudio)"));

        var result = await notifier.NotifyAsync("hi");

        Assert.True(result.SoundPlayed);
        Assert.NotNull(result.Sound);
        Assert.Equal("MP3 (NAudio)", result.Sound!.Transport);
    }

    [Fact]
    public async Task NotifyAsync_SoundPlayerFails_PropagatesFalseAndKeepsError()
    {
        var notifier = new PsstNotifier(Array.Empty<ISmsClient>(), SoundFail("no audio device"));

        var result = await notifier.NotifyAsync("hi");

        Assert.False(result.SoundPlayed);
        Assert.NotNull(result.Sound);
        Assert.Equal("no audio device", result.Sound!.Error);
    }

    [Fact]
    public async Task NotifyAsync_FirstTransportSucceeds_DoesNotCallSecond()
    {
        var first = new StubClient("Twilio", success: true);
        var second = new StubClient("Email", success: true);

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
        Assert.Single(result.SmsAttempts);
        Assert.True(result.AnySmsSent);
        Assert.Equal("Twilio", result.FirstSuccess!.Transport);
    }

    [Fact]
    public async Task NotifyAsync_FirstTransportFails_FallsBackToSecond()
    {
        var first = new StubClient("Twilio", success: false, detail: "401");
        var second = new StubClient("Email", success: true);

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(2, result.SmsAttempts.Count);
        Assert.False(result.SmsAttempts[0].Success);
        Assert.True(result.SmsAttempts[1].Success);
        Assert.Equal("Email", result.FirstSuccess!.Transport);
    }

    [Fact]
    public async Task NotifyAsync_AllTransportsFail_ReportsAllAttempts()
    {
        var first = new StubClient("Twilio", success: false, detail: "401");
        var second = new StubClient("Email", success: false, detail: "smtp boom");

        var notifier = new PsstNotifier(new[] { first, second }, SoundFail());
        var result = await notifier.NotifyAsync("hi", silent: true);

        Assert.Equal(2, result.SmsAttempts.Count);
        Assert.False(result.AnySmsSent);
        Assert.Null(result.FirstSuccess);
    }

    [Fact]
    public async Task NotifyAsync_PassesMessageToTransport()
    {
        var client = new StubClient("X", success: true);
        var notifier = new PsstNotifier(new[] { client }, SoundFail());

        await notifier.NotifyAsync("hello world", silent: true);

        Assert.Equal("hello world", client.LastMessage);
    }

    [Fact]
    public async Task NotifyAsync_PropagatesCancellationToken()
    {
        var client = new StubClient("X", success: true);
        var notifier = new PsstNotifier(new[] { client }, SoundFail());
        using var cts = new CancellationTokenSource();

        await notifier.NotifyAsync("x", silent: true, cancellationToken: cts.Token);

        Assert.Equal(cts.Token, client.LastToken);
    }

    [Fact]
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

        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(180),
            $"expected concurrent execution but took {sw.ElapsedMilliseconds}ms");

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
