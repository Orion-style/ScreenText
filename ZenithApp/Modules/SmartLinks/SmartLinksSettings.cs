namespace ZenithApp.Modules.SmartLinks;

public sealed class SmartLinksSettings
{
    public bool AutoOpenUrls { get; set; } = true;
    public bool AutoOpenQrUrls { get; set; } = false;
    public string DefaultTranslateLanguage { get; set; } = "ru";
    public bool ShowPopupForPlainText { get; set; } = true;
    public int PopupTimeoutSeconds { get; set; } = 8;
}
