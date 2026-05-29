using Xunit;

namespace GdriveHandler.Tests;

/// <summary>
/// Tests for <see cref="Settings"/>: INI parsing, serialization, alias resolution,
/// and default values when sections are missing.
/// </summary>
public class SettingsTests
{
    // ------------------------------------------------------------------ round-trip

    [Fact]
    public void RoundTrip_DefaultSettings_PreservesValues()
    {
        var original = new Settings();
        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.Equal(original.OpenInNewWindow, parsed.OpenInNewWindow);
        Assert.Equal(original.IncludeEdge, parsed.IncludeEdge);
        Assert.Empty(parsed.Aliases);
    }

    [Fact]
    public void RoundTrip_WithAliases_PreservesAllAliases()
    {
        var original = new Settings
        {
            OpenInNewWindow = true,
            IncludeEdge = false,
        };
        original.Aliases["old@gmail.com"] = "new@domain.fyi";
        original.Aliases["other@gmail.com"] = "other@workspace.com";

        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.True(parsed.OpenInNewWindow);
        Assert.False(parsed.IncludeEdge);
        Assert.Equal(2, parsed.Aliases.Count);
        Assert.Equal("new@domain.fyi", parsed.Aliases["old@gmail.com"]);
        Assert.Equal("other@workspace.com", parsed.Aliases["other@gmail.com"]);
    }

    // ------------------------------------------------------------------ parsing: tolerances

    [Fact]
    public void Parse_IgnoresSemicolonComments()
    {
        const string ini = """
            [settings]
            ; this is a comment
            openInNewWindow=true
            """;
        var s = Settings.Parse(ini);
        Assert.True(s.OpenInNewWindow);
    }

    [Fact]
    public void Parse_IgnoresHashComments()
    {
        const string ini = """
            [settings]
            # another comment
            includeEdge=false
            """;
        var s = Settings.Parse(ini);
        Assert.False(s.IncludeEdge);
    }

    [Fact]
    public void Parse_IgnoresBlankLines()
    {
        const string ini = "\n\n[settings]\n\nopenInNewWindow=true\n\n";
        var s = Settings.Parse(ini);
        Assert.True(s.OpenInNewWindow);
    }

    [Fact]
    public void Parse_TrimsKeyAndValueWhitespace()
    {
        const string ini = "[settings]\n  openInNewWindow  =  true  \n";
        var s = Settings.Parse(ini);
        Assert.True(s.OpenInNewWindow);
    }

    [Fact]
    public void Parse_SectionNameIsCaseInsensitive()
    {
        const string ini = "[SETTINGS]\nopenInNewWindow=true\n[ALIASES]\nfoo@a.com=bar@b.com\n";
        var s = Settings.Parse(ini);
        Assert.True(s.OpenInNewWindow);
        Assert.Equal("bar@b.com", s.Aliases["foo@a.com"]);
    }

    [Fact]
    public void Parse_BoolValues_AcceptsVariousForms()
    {
        Assert.True(Settings.Parse("[settings]\nopenInNewWindow=1\n").OpenInNewWindow);
        Assert.True(Settings.Parse("[settings]\nopenInNewWindow=yes\n").OpenInNewWindow);
        Assert.True(Settings.Parse("[settings]\nopenInNewWindow=TRUE\n").OpenInNewWindow);
        Assert.False(Settings.Parse("[settings]\nopenInNewWindow=0\n").OpenInNewWindow);
        Assert.False(Settings.Parse("[settings]\nopenInNewWindow=no\n").OpenInNewWindow);
        Assert.False(Settings.Parse("[settings]\nopenInNewWindow=FALSE\n").OpenInNewWindow);
    }

    // ------------------------------------------------------------------ defaults when sections missing

    [Fact]
    public void Parse_EmptyString_ReturnsDefaults()
    {
        var s = Settings.Parse(string.Empty);
        Assert.Equal(AppConstants.OpenInNewWindow, s.OpenInNewWindow);
        Assert.True(s.IncludeEdge);
        Assert.Empty(s.Aliases);
    }

    [Fact]
    public void Parse_MissingSettingsSection_HasDefaultBooleans()
    {
        const string ini = "[aliases]\nold@g.com=new@d.fyi\n";
        var s = Settings.Parse(ini);
        Assert.Equal(AppConstants.OpenInNewWindow, s.OpenInNewWindow);
        Assert.True(s.IncludeEdge);
        Assert.Single(s.Aliases);
    }

    [Fact]
    public void Parse_MissingAliasesSection_HasEmptyDict()
    {
        const string ini = "[settings]\nincludeEdge=false\n";
        var s = Settings.Parse(ini);
        Assert.Empty(s.Aliases);
        Assert.False(s.IncludeEdge);
    }

    // ------------------------------------------------------------------ alias resolution

    [Fact]
    public void ResolveAlias_KnownKey_ReturnsMappedValue()
    {
        var s = new Settings();
        s.Aliases["old@gmail.com"] = "converted@workspace.fyi";

        var result = s.ResolveAlias("old@gmail.com");
        Assert.Equal("converted@workspace.fyi", result);
    }

    [Fact]
    public void ResolveAlias_CaseInsensitive()
    {
        var s = new Settings();
        s.Aliases["Old@Gmail.COM"] = "target@domain.fyi";

        Assert.Equal("target@domain.fyi", s.ResolveAlias("old@gmail.com"));
        Assert.Equal("target@domain.fyi", s.ResolveAlias("OLD@GMAIL.COM"));
    }

    [Fact]
    public void ResolveAlias_UnknownKey_ReturnsNull()
    {
        var s = new Settings();
        s.Aliases["other@gmail.com"] = "x@y.z";

        Assert.Null(s.ResolveAlias("nobody@gmail.com"));
    }

    [Fact]
    public void ResolveAlias_EmptyAliases_ReturnsNull()
    {
        var s = new Settings();
        Assert.Null(s.ResolveAlias("any@email.com"));
    }

    [Fact]
    public void ResolveAlias_TrimsInputEmail()
    {
        var s = new Settings();
        s.Aliases["user@domain.com"] = "mapped@other.com";

        Assert.Equal("mapped@other.com", s.ResolveAlias("  user@domain.com  "));
    }

    // ------------------------------------------------------------------ language setting

    [Fact]
    public void RoundTrip_Language_En_PreservesValue()
    {
        var original = new Settings { Language = "en" };
        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.Equal("en", parsed.Language);
    }

    [Fact]
    public void RoundTrip_Language_Tr_PreservesValue()
    {
        var original = new Settings { Language = "tr" };
        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.Equal("tr", parsed.Language);
    }

    [Fact]
    public void Parse_Language_Tr_CaseInsensitive()
    {
        var s = Settings.Parse("[settings]\nlanguage=TR\n");
        Assert.Equal("tr", s.Language);
    }

    [Fact]
    public void Parse_Language_UnknownValue_FallsBackToEn()
    {
        var s = Settings.Parse("[settings]\nlanguage=fr\n");
        Assert.Equal("en", s.Language);
    }

    [Fact]
    public void Parse_Language_Missing_DefaultsToEn()
    {
        var s = Settings.Parse("[settings]\nopenInNewWindow=false\n");
        Assert.Equal("en", s.Language);
    }

    [Fact]
    public void RoundTrip_Language_DoesNotBreakExistingKeys()
    {
        var original = new Settings
        {
            OpenInNewWindow = true,
            IncludeEdge = false,
            Language = "tr",
        };
        original.Aliases["a@b.com"] = "c@d.com";

        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.True(parsed.OpenInNewWindow);
        Assert.False(parsed.IncludeEdge);
        Assert.Equal("tr", parsed.Language);
        Assert.Equal("c@d.com", parsed.Aliases["a@b.com"]);
    }

    // ------------------------------------------------------------------ advanced mode

    [Fact]
    public void RoundTrip_AdvancedMode_True_PreservesValue()
    {
        var original = new Settings { AdvancedMode = true };
        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.True(parsed.AdvancedMode);
    }

    [Fact]
    public void RoundTrip_AdvancedMode_False_PreservesValue()
    {
        var original = new Settings { AdvancedMode = false };
        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.False(parsed.AdvancedMode);
    }

    [Fact]
    public void Parse_AdvancedMode_Missing_DefaultsFalse()
    {
        var s = Settings.Parse("[settings]\nopenInNewWindow=false\n");
        Assert.False(s.AdvancedMode);
    }

    [Fact]
    public void RoundTrip_AdvancedMode_DoesNotBreakExistingKeys()
    {
        var original = new Settings
        {
            OpenInNewWindow = true,
            IncludeEdge = false,
            Language = "tr",
            AdvancedMode = true,
        };
        original.Aliases["x@b.com"] = "y@d.com";

        var ini = Settings.ToIni(original);
        var parsed = Settings.Parse(ini);

        Assert.True(parsed.OpenInNewWindow);
        Assert.False(parsed.IncludeEdge);
        Assert.Equal("tr", parsed.Language);
        Assert.True(parsed.AdvancedMode);
        Assert.Equal("y@d.com", parsed.Aliases["x@b.com"]);
    }

    // ------------------------------------------------------------------ ToIni format checks

    [Fact]
    public void ToIni_ContainsSettingsSection()
    {
        var ini = Settings.ToIni(new Settings());
        Assert.Contains("[settings]", ini);
        Assert.Contains("openInNewWindow=", ini);
        Assert.Contains("includeEdge=", ini);
        Assert.Contains("advancedMode=", ini);
    }

    [Fact]
    public void ToIni_ContainsAliasesSection()
    {
        var ini = Settings.ToIni(new Settings());
        Assert.Contains("[aliases]", ini);
    }

    [Fact]
    public void ToIni_AliasLinesAreFromEqualsTo()
    {
        var s = new Settings();
        s.Aliases["a@b.com"] = "c@d.com";
        var ini = Settings.ToIni(s);
        Assert.Contains("a@b.com=c@d.com", ini);
    }
}
