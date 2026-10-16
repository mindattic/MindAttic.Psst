namespace MindAttic.Psst.Tests.Configuration;

using MindAttic.Psst.Configuration;
using MindAttic.Vault.Paths;
using NUnit.Framework;

public class PsstConfigurationSourcesTests
{
    [Test]
    public void GetAppDataDirectory_LandsUnderRoamingMindAtticPsst()
    {
        var path = PsstConfigurationSources.GetAppDataDirectory();

        Assert.That(path, Does.EndWith(Path.Combine("MindAttic", "Psst")));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.That(path, Does.StartWith(appData));
    }

    [Test]
    public void GetSettingsPath_IsSettingsJsonUnderAppData()
    {
        var path = PsstConfigurationSources.GetSettingsPath();
        Assert.That(path, Does.EndWith("settings.json"));
        Assert.That(path, Does.StartWith(PsstConfigurationSources.GetAppDataDirectory()));
    }

    [Test]
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
            Assert.That(path, Is.EqualTo(Path.Combine(tempRoot, "Psst")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, prior);
        }
    }
}
