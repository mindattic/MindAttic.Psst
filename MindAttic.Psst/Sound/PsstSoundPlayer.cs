namespace MindAttic.Psst.Sound;

using System.Media;
using System.Reflection;
using System.Runtime.Versioning;

/// <summary>
/// Plays the embedded attention-getter clip using <see cref="SoundPlayer"/>.
///
/// The clip is shipped as WAV (PCM, 22 kHz, mono) so that <see cref="SoundPlayer"/>
/// can render it with no codec dependency — Windows' built-in MCI MPEG driver
/// is unreliable across editions, and WAV is universally supported.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PsstSoundPlayer
{
    private const string ResourceName = "MindAttic.Psst.Sound.icq-uh-oh.wav";

    /// <summary>Last failure reason from a Play() call — empty on success.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// Play the embedded WAV. When <paramref name="waitForCompletion"/> is true,
    /// blocks until the clip finishes (PlaySync). Otherwise schedules async
    /// playback and returns immediately. Returns false on any error; the
    /// reason lives in <see cref="LastError"/>.
    /// </summary>
    public static bool Play(bool waitForCompletion = true)
    {
        LastError = "";
        if (!OperatingSystem.IsWindows())
        {
            LastError = "not windows";
            return false;
        }

        try
        {
            using var stream = LoadResourceStream();
            if (stream is null)
            {
                LastError = $"embedded resource '{ResourceName}' not found";
                return false;
            }

            var player = new SoundPlayer(stream);
            if (waitForCompletion) player.PlaySync();
            else player.Play();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static Stream? LoadResourceStream()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceStream(ResourceName);
    }
}
