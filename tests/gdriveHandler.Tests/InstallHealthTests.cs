using Xunit;

namespace GdriveHandler.Tests;

public class InstallHealthTests
{
    [Fact]
    public void IsInstallHealthy_ReturnsTrue_WhenExeExistsAndAllExtensionsUseExpectedProgIds()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            ext => (string?)AppConstants.ProgIdForExtension(ext),
            StringComparer.OrdinalIgnoreCase);

        Assert.True(Installer.IsInstallHealthy(installedExeExists: true, registrations));
    }

    [Fact]
    public void IsInstallHealthy_ReturnsFalse_WhenInstalledExeIsMissing()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            ext => (string?)AppConstants.ProgIdForExtension(ext),
            StringComparer.OrdinalIgnoreCase);

        Assert.False(Installer.IsInstallHealthy(installedExeExists: false, registrations));
    }

    [Fact]
    public void IsInstallHealthy_ReturnsFalse_WhenAnyExtensionIsMissingOrWrong()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            ext => (string?)AppConstants.ProgIdForExtension(ext),
            StringComparer.OrdinalIgnoreCase);
        registrations[".gdoc"] = null;

        Assert.False(Installer.IsInstallHealthy(installedExeExists: true, registrations));

        registrations[".gdoc"] = "Other.App";

        Assert.False(Installer.IsInstallHealthy(installedExeExists: true, registrations));
    }

    [Fact]
    public void IsInstallHealthy_ReturnsFalse_ForLegacySharedProgId()
    {
        var registrations = AppConstants.AllExtensions.ToDictionary(
            ext => ext,
            _ => (string?)AppConstants.ProgId,
            StringComparer.OrdinalIgnoreCase);

        Assert.False(Installer.IsInstallHealthy(installedExeExists: true, registrations));
    }

    [Theory]
    [InlineData(".gdoc", "devSpaceOB.gdriveHandler.gdoc", @"Assets\FileIcons\Docs.ico")]
    [InlineData(".gsheet", "devSpaceOB.gdriveHandler.gsheet", @"Assets\FileIcons\Sheets.ico")]
    [InlineData(".gslides", "devSpaceOB.gdriveHandler.gslides", @"Assets\FileIcons\Slides.ico")]
    [InlineData(".gform", "devSpaceOB.gdriveHandler.gform", @"Assets\FileIcons\Forms.ico")]
    [InlineData(".gsite", "devSpaceOB.gdriveHandler.gsite", @"Assets\FileIcons\Sites.ico")]
    [InlineData(".glink", "devSpaceOB.gdriveHandler.glink", @"Assets\FileIcons\Drive.ico")]
    [InlineData(".gdraw", "devSpaceOB.gdriveHandler.gdraw", @"Assets\App.ico")]
    public void ExtensionMetadata_UsesSpecificIconsWhenAvailable_AndAppIconFallback(
        string extension,
        string expectedProgId,
        string expectedIcon)
    {
        Assert.Equal(expectedProgId, AppConstants.ProgIdForExtension(extension));
        Assert.Equal(expectedIcon, AppConstants.IconAssetForExtension(extension));
    }

    [Fact]
    public void ResolveIcon_UsesInstalledIconFile_WhenItExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "gdrivehandler-tests", Guid.NewGuid().ToString("N"));
        var exePath = Path.Combine(root, "gdriveHandler.exe");
        var iconPath = Path.Combine(root, "Assets", "FileIcons", "Docs.ico");

        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        File.WriteAllBytes(iconPath, [0]);

        try
        {
            Assert.Equal($"{iconPath},0", Installer.ResolveIcon(exePath, ".gdoc"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveIcon_FallsBackToExe_WhenIconFileIsMissing()
    {
        var exePath = Path.Combine(Path.GetTempPath(), "gdriveHandler.exe");

        Assert.Equal($"{exePath},0", Installer.ResolveIcon(exePath, ".gdoc"));
    }

    [Fact]
    public void BuildUninstallCommands_UseQuietOnlyForQuietCommand()
    {
        var exePath = Path.Combine(Path.GetTempPath(), "gdriveHandler.exe");

        Assert.Equal($"\"{exePath}\" --uninstall", Installer.BuildUninstallString(exePath, quiet: false));
        Assert.Equal($"\"{exePath}\" --uninstall --quiet", Installer.BuildUninstallString(exePath, quiet: true));
    }

    [Fact]
    public void IsInstalledExecutablePath_ReturnsTrue_ForKnownInstallLocations()
    {
        Assert.True(Installer.IsInstalledExecutablePath(AppConstants.InstalledExePath));
        Assert.True(Installer.IsInstalledExecutablePath(AppConstants.SystemExePath));
    }

    [Fact]
    public void IsInstalledExecutablePath_ReturnsFalse_ForPortableLocation()
    {
        var portableExe = Path.Combine(Path.GetTempPath(), "gdriveHandler", "gdriveHandler.exe");

        Assert.False(Installer.IsInstalledExecutablePath(portableExe));
        Assert.False(Installer.IsInstalledExecutablePath(null));
    }
}
