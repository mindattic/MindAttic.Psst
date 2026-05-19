namespace MindAttic.Psst.Sound;

using System.Media;
using System.Reflection;
using System.Runtime.Versioning;
using NAudio.Wave;

/// <summary>
/// Plays the embedded attention-getter clip.
///
/// <para>Two transports, tried in order:</para>
/// <list type="number">
///   <item><description>
///     <b>MP3 via NAudio.</b> <see cref="Mp3FileReader"/> decodes the embedded
///     stream and <see cref="WaveOutEvent"/> renders it through WASAPI. This
///     is the preferred path — keeps the source clip in its native format and
///     avoids the rebuild-step needed by WAV.
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

    /// <summary>Last failure reason from a Play() call — empty on success.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Which transport actually produced sound on the last successful call.</summary>
    public static string LastTransport { get; private set; } = "";

    /// <summary>
    /// Play the clip. When <paramref name="waitForCompletion"/> is true, blocks
    /// until playback ends. Tries MP3 first, then WAV. Returns false only when
    /// both paths failed; <see cref="LastError"/> aggregates the diagnostics.
    /// </summary>
    public static bool Play(bool waitForCompletion = true)
    {
        LastError = "";
        LastTransport = "";

        if (!OperatingSystem.IsWindows())
        {
            LastError = "not windows";
            return false;
        }

        if (TryPlayMp3(waitForCompletion, out var mp3Err))
        {
            LastTransport = "MP3 (NAudio)";
            return true;
        }

        if (TryPlayWav(waitForCompletion, out var wavErr))
        {
            LastTransport = "WAV (SoundPlayer)";
            return true;
        }

        LastError = $"MP3: {mp3Err}; WAV: {wavErr}";
        return false;
    }

    private static bool TryPlayMp3(bool wait, out string error)
    {
        error = "";
        try
        {
            using var stream = LoadResource(Mp3Resource);
            if (stream is null)
            {
                error = $"embedded resource '{Mp3Resource}' not found";
                return false;
            }

            // NAudio's Mp3FileReader needs a seekable stream — copy to memory.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            using var reader = new Mp3FileReader(ms);
            using var output = new WaveOutEvent();
            output.Init(reader);
            output.Play();

            if (wait)
            {
                // Loop on PlaybackState rather than relying solely on the
                // PlaybackStopped event — the using-block disposes the sink
                // synchronously, and we want to keep the foreground process
                // alive until the clip finishes.
                while (output.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryPlayWav(bool wait, out string error)
    {
        error = "";
        try
        {
            using var stream = LoadResource(WavResource);
            if (stream is null)
            {
                error = $"embedded resource '{WavResource}' not found";
                return false;
            }

            var player = new SoundPlayer(stream);
            if (wait) player.PlaySync();
            else player.Play();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Stream? LoadResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
}
