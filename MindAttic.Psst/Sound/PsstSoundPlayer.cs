namespace MindAttic.Psst.Sound;

using System.Media;
using System.Reflection;
using System.Runtime.Versioning;
using NAudio.Wave;

/// <summary>Outcome of a single <see cref="PsstSoundPlayer.PlayAsync"/> call.</summary>
public sealed record PsstPlayResult(bool Success, string Transport, string Error)
{
    public static PsstPlayResult Ok(string transport) => new(true, transport, "");
    public static PsstPlayResult Fail(string error) => new(false, "", error);
}

/// <summary>
/// Plays the embedded attention-getter clip.
///
/// <para>Two transports, tried in order:</para>
/// <list type="number">
///   <item><description>
///     <b>MP3 via NAudio.</b> <see cref="Mp3FileReader"/> decodes the embedded
///     stream and <see cref="WaveOutEvent"/> renders it through WASAPI.
///   </description></item>
///   <item><description>
///     <b>WAV via <see cref="SoundPlayer"/>.</b> Pure managed, no codec
///     dependency. Used when NAudio is unavailable or initialization fails.
///   </description></item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public static class PsstSoundPlayer
{
    private const string Mp3Resource = "MindAttic.Psst.Sound.icq-uh-oh.mp3";
    private const string WavResource = "MindAttic.Psst.Sound.icq-uh-oh.wav";

    /// <summary>
    /// Play the clip. Awaits until playback ends or <paramref name="cancellationToken"/>
    /// fires. Tries MP3 first, then WAV; returns the failing reason from both
    /// transports when neither works.
    /// </summary>
    public static async Task<PsstPlayResult> PlayAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return PsstPlayResult.Fail("not windows");

        var mp3 = await TryPlayMp3Async(cancellationToken);
        if (mp3.Success) return mp3;

        var wav = TryPlayWav();
        if (wav.Success) return wav;

        return PsstPlayResult.Fail($"MP3: {mp3.Error}; WAV: {wav.Error}");
    }

    private static async Task<PsstPlayResult> TryPlayMp3Async(CancellationToken cancellationToken)
    {
        Stream? resource = null;
        MemoryStream? ms = null;
        Mp3FileReader? reader = null;
        WaveOutEvent? output = null;
        try
        {
            resource = LoadResource(Mp3Resource);
            if (resource is null)
                return PsstPlayResult.Fail($"embedded resource '{Mp3Resource}' not found");

            // Mp3FileReader needs a seekable stream — copy to memory, then keep
            // the memory stream alive for the lifetime of the reader (the
            // reader references the underlying stream during decoding).
            ms = new MemoryStream();
            await resource.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            reader = new Mp3FileReader(ms);
            output = new WaveOutEvent();
            output.Init(reader);

            var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) => done.TrySetResult(args.Exception);

            using var registration = cancellationToken.Register(() =>
            {
                try { output?.Stop(); } catch { /* sink disposed */ }
                done.TrySetCanceled(cancellationToken);
            });

            output.Play();
            var error = await done.Task;
            if (error is not null)
                return PsstPlayResult.Fail(error.Message);
            return PsstPlayResult.Ok("MP3 (NAudio)");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PsstPlayResult.Fail(ex.Message);
        }
        finally
        {
            output?.Dispose();
            reader?.Dispose();
            ms?.Dispose();
            resource?.Dispose();
        }
    }

    private static PsstPlayResult TryPlayWav()
    {
        try
        {
            using var stream = LoadResource(WavResource);
            if (stream is null)
                return PsstPlayResult.Fail($"embedded resource '{WavResource}' not found");

            var player = new SoundPlayer(stream);
            player.PlaySync();
            return PsstPlayResult.Ok("WAV (SoundPlayer)");
        }
        catch (Exception ex)
        {
            return PsstPlayResult.Fail(ex.Message);
        }
    }

    private static Stream? LoadResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
}
