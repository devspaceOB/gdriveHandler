using GdriveHandler;
using Xunit;

namespace GdriveHandler.Tests;

public class UrlBuilderTests
{
    [Theory]
    [InlineData("1abcDEFghij_klmnop", "1abcDEFghij_klmnop")]                                  // bare id
    [InlineData("spreadsheet:1abcDEFghij_klmnop", "1abcDEFghij_klmnop")]                       // type-prefixed
    [InlineData("https://docs.google.com/spreadsheets/d/1abcDEFghij_klmnop/edit", "1abcDEFghij_klmnop")] // /d/ path
    [InlineData("https://drive.google.com/open?id=1abcDEFghij_klmnop", "1abcDEFghij_klmnop")]  // ?id=
    public void NormalizeDocId_extracts_id_from_known_forms(string input, string expected)
    {
        Assert.Equal(expected, UrlBuilder.NormalizeDocId(input));
    }

    [Theory]
    [InlineData("short")]   // below the 10-char minimum
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeDocId_returns_null_for_unusable_input(string? input)
    {
        Assert.Null(UrlBuilder.NormalizeDocId(input));
    }

    [Fact]
    public void BuildFinalUrl_uses_template_for_gsheet_doc_id()
    {
        var info = new ShortcutInfo(".gsheet", "u@example.com", Url: null, DocId: "1abcDEFghij_klmnop", ResourceKey: null);
        Assert.Equal("https://docs.google.com/spreadsheets/d/1abcDEFghij_klmnop/edit", UrlBuilder.BuildFinalUrl(info));
    }

    [Fact]
    public void BuildFinalUrl_prefers_absolute_url_when_present()
    {
        var url = "https://docs.google.com/document/d/1abcDEFghij_klmnop/edit";
        var info = new ShortcutInfo(".gdoc", "u@example.com", url, DocId: "ignored_other_id", ResourceKey: null);
        Assert.Equal(url, UrlBuilder.BuildFinalUrl(info));
    }

    [Fact]
    public void BuildFinalUrl_appends_resource_key_when_absent()
    {
        var info = new ShortcutInfo(".gsheet", null, Url: null, DocId: "1abcDEFghij_klmnop", ResourceKey: "rk123");
        var result = UrlBuilder.BuildFinalUrl(info);
        Assert.Equal("https://docs.google.com/spreadsheets/d/1abcDEFghij_klmnop/edit?resourcekey=rk123", result);
    }

    [Fact]
    public void BuildFinalUrl_does_not_duplicate_existing_resource_key()
    {
        var url = "https://docs.google.com/document/d/1abcDEFghij_klmnop/edit?resourcekey=already";
        var info = new ShortcutInfo(".gdoc", null, url, DocId: null, ResourceKey: "different");
        Assert.Equal(url, UrlBuilder.BuildFinalUrl(info));
    }

    [Fact]
    public void BuildFinalUrl_falls_back_to_drive_open_for_unknown_extension()
    {
        var info = new ShortcutInfo(".gjam", null, Url: null, DocId: "1abcDEFghij_klmnop", ResourceKey: null);
        Assert.Equal("https://drive.google.com/open?id=1abcDEFghij_klmnop", UrlBuilder.BuildFinalUrl(info));
    }

    [Fact]
    public void BuildFinalUrl_passes_through_arbitrary_https_for_generic_link()
    {
        var info = new ShortcutInfo(".glink", null, "https://example.com/some/page", DocId: null, ResourceKey: null);
        Assert.Equal("https://example.com/some/page", UrlBuilder.BuildFinalUrl(info));
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://host/x")]
    public void BuildFinalUrl_rejects_non_web_schemes(string url)
    {
        var info = new ShortcutInfo(".glink", null, url, DocId: null, ResourceKey: null);
        Assert.Null(UrlBuilder.BuildFinalUrl(info));
    }

    [Fact]
    public void BuildFinalUrl_returns_null_when_no_url_or_doc_id()
    {
        var info = new ShortcutInfo(".gdoc", "u@example.com", Url: null, DocId: null, ResourceKey: null);
        Assert.Null(UrlBuilder.BuildFinalUrl(info));
    }
}
