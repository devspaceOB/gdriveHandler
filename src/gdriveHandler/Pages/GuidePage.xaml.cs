using Microsoft.UI.Xaml.Controls;

namespace GdriveHandler.Pages;

public sealed partial class GuidePage : Page
{
    public string SampleJson { get; } =
        "{\n" +
        "  \"doc_id\": \"1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms\",\n" +
        "  \"email\":  \"you@example.com\",\n" +
        "  \"resource_key\": \"\"\n" +
        "}";

    public GuidePage()
    {
        InitializeComponent();
        ExtensionsText.Text = string.Join("  ",
            AppConstants.AllExtensions.OrderBy(e => e, StringComparer.Ordinal));
    }
}
