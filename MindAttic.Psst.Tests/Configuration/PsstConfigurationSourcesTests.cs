namespace MindAttic.Psst.Tests.Configuration;

using MindAttic.Psst.Configuration;
using MindAttic.Vault.Paths;
using Xunit;

public class PsstConfigurationSourcesTests
{
    [Fact]
    public void GetAppDataDirectory_LandsUnderRoamingMindAtticPsst()
    {
        var path = PsstConfigurationSources.GetAppDataDirectory();

        Assert.EndsWith(Path.Combine("MindAttic", "Psst"), path);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, path);
    }

    [Fact]
    public void GetSettingsPath_IsSettingsJsonUnderAppData()
    {
        var path = PsstConfigurationSources.GetSettingsPath();
        Assert.EndsWith("settings.json", path);
        Assert.StartsWith(PsstConfigurationSources.GetAppDataDirectory(), path);
    }

    [Fact]
    public void GetAppDataDirectory_RespectsVaultRoamingRootOverride()
    {
        // The Vault test convention: MINDATTIC_VAULT_ROAMING_ROOT redirects
        // every per-app folder. Confirms PsstConfigurationSources now flows
        // through VaultPaths instead of hand-rolling %APPDATA%.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"psst-vault-{Guid.NewGuid():N}");
        var prior = Environment.GetEnvironmentVariable(VaultPaths.RoamingRootEnvVar);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, tempRoot);
        try
        {
            var path = PsstConfigurationSources.GetAppDataDirectory();
            Assert.Equal(Path.Combine(tempRoot, "Psst"), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, prior);
        }
    }
}
