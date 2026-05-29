using GdriveHandler;
using Xunit;

namespace GdriveHandler.Tests;

public class ShortcutParserTests
{
    [Fact]
    public void Parses_modern_gsheet_json()
    {
        const string json = """
            {
              "doc_id": "1FakeSheetDocId_abcdefghijklmnop",
              "email": "owner@example.com",
              "resource_key": "fake-resource-key"
            }
            """;

        var info = ShortcutParser.Parse(".gsheet", json);

        Assert.Equal("owner@example.com", info.Email);
        Assert.Equal("1FakeSheetDocId_abcdefghijklmnop", info.DocId);
        Assert.Equal("fake-resource-key", info.ResourceKey);
    }

    [Fact]
    public void Honors_alias_keys_for_email_and_doc_id()
    {
        const string json = """
            { "user_email": "alt@example.com", "fileId": "1AltDocId_abcdefghijklmnop" }
            """;

        var info = ShortcutParser.Parse(".gdoc", json);

        Assert.Equal("alt@example.com", info.Email);
        Assert.Equal("1AltDocId_abcdefghijklmnop", info.DocId);
    }

    [Fact]
    public void Extracts_url_from_json()
    {
        const string json = """
            { "url": "https://docs.google.com/document/d/1DocId_abcdefghijklmnop/edit", "email": "u@example.com" }
            """;

        var info = ShortcutParser.Parse(".gdoc", json);

        Assert.Equal("https://docs.google.com/document/d/1DocId_abcdefghijklmnop/edit", info.Url);
    }

    [Fact]
    public void Falls_back_to_regex_for_non_json_content()
    {
        // Not valid JSON; parser should still recover the email and URL.
        const string text = "garbage prefix owner=someone@example.com see https://docs.google.com/document/d/1RegexDocId_abcdefghij/edit trailing";

        var info = ShortcutParser.Parse(".gdoc", text);

        Assert.Equal("someone@example.com", info.Email);
        Assert.Equal("https://docs.google.com/document/d/1RegexDocId_abcdefghij/edit", info.Url);
    }

    [Fact]
    public void Recovers_resource_key_from_url_query()
    {
        const string json = """
            { "url": "https://docs.google.com/document/d/1DocId_abcdefghijklmnop/edit?resourcekey=rk-from-url", "email": "u@example.com" }
            """;

        var info = ShortcutParser.Parse(".gdoc", json);

        Assert.Equal("rk-from-url", info.ResourceKey);
    }

    [Fact]
    public void Missing_fields_are_null_not_empty()
    {
        var info = ShortcutParser.Parse(".gslides", "{ }");

        Assert.Null(info.Email);
        Assert.Null(info.Url);
        Assert.Null(info.DocId);
        Assert.Null(info.ResourceKey);
        Assert.Equal(".gslides", info.Extension);
    }

    [Fact]
    public void End_to_end_gsheet_builds_expected_url()
    {
        const string json = """
            { "doc_id": "1FakeSheetDocId_abcdefghijklmnop", "email": "owner@example.com", "resource_key": "rk9" }
            """;

        var info = ShortcutParser.Parse(".gsheet", json);
        var url = UrlBuilder.BuildFinalUrl(info);

        Assert.Equal("https://docs.google.com/spreadsheets/d/1FakeSheetDocId_abcdefghijklmnop/edit?resourcekey=rk9", url);
    }
}
