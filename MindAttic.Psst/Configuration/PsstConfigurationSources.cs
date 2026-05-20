namespace MindAttic.Psst.Configuration;

using MindAttic.Vault.Paths;

/// <summary>
/// Resolves the on-disk Psst settings file via MindAttic.Vault's path math.
///
/// <para>Psst stores its primary configuration at
/// <c>%APPDATA%/MindAttic/Psst/settings.json</c>. The folder layout matches the
/// Vault per-app convention (<see cref="VaultPaths.RoamingBucket"/>) so the
/// same path resolves correctly when tests redirect the roaming root via
/// <c>MINDATTIC_VAULT_ROAMING_ROOT</c>.</para>
/// </summary>
public static class PsstConfigurationSources
{
    /// <summary>The Vault bucket folder used for Psst's per-app settings.</summary>
    public const string AppFolder = "Psst";

    /// <summary>Directory under the MindAttic roaming root that holds Psst's settings file.</summary>
    public static string GetAppDataDirectory() => VaultPaths.RoamingBucket(AppFolder);

    /// <summary>Full path of the primary settings.json (may not exist yet).</summary>
    public static string GetSettingsPath() =>
        Path.Combine(GetAppDataDirectory(), "settings.json");
}
