using Xunit;

namespace GdriveHandler.Tests;

public class InstallHealthTests
{
    [Fact]
    public void IsInstallHealthy_ReturnsTrue_WhenExeExistsAndAllExtensionsUseProgId()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            _ => (string?)AppConstants.ProgId,
            StringComparer.OrdinalIgnoreCase);

        Assert.True(Installer.IsInstallHealthy(installedExeExists: true, registrations));
    }

    [Fact]
    public void IsInstallHealthy_ReturnsFalse_WhenInstalledExeIsMissing()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            _ => (string?)AppConstants.ProgId,
            StringComparer.OrdinalIgnoreCase);

        Assert.False(Installer.IsInstallHealthy(installedExeExists: false, registrations));
    }

    [Fact]
    public void IsInstallHealthy_ReturnsFalse_WhenAnyExtensionIsMissingOrWrong()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            _ => (string?)AppConstants.ProgId,
            StringComparer.OrdinalIgnoreCase);
        registrations[".gdoc"] = null;

        Assert.False(Installer.IsInstallHealthy(installedExeExists: true, registrations));

        registrations[".gdoc"] = "Other.App";

        Assert.False(Installer.IsInstallHealthy(installedExeExists: true, registrations));
    }
}
