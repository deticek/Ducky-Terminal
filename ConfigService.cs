using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace terminal;

public sealed class AppConfig
{
public int Version { get; set; } = 1;

public ShellSelectionConfig Shell { get; set; } = new();
public AppearanceConfig Appearance { get; set; } = new();
public WindowConfig Window { get; set; } = new();
public NavbarConfig Navbar { get; set; } = new();
public LayoutConfig Layout { get; set; } = new();
public BehaviorConfig Behavior { get; set; } = new();
public KeybindingsConfig Keybindings { get; set; } = new();


}

public sealed class AppearanceConfig
{
public string Theme { get; set; } = "WindowsXP";
public string FontFamily { get; set; } = "Consolas";
public double FontSize { get; set; } = 14;
public string CursorStyle { get; set; } = "Block";
public bool CursorBlink { get; set; } = true;
public double Padding { get; set; } = 8;
public double WindowOpacity { get; set; } = 1.0;
}

public sealed class WindowConfig
{
public bool ShowTitleBar { get; set; } = true;
public bool ShowNavbar { get; set; } = true;
public bool StartMaximized { get; set; } = false;
public bool RememberWindowBounds { get; set; } = true;
}

public sealed class NavbarConfig
{
public bool Visible { get; set; } = true;
public string Position { get; set; } = "Top";
public double Height { get; set; } = 48;
public string Background { get; set; } = "#D4D0C8";
public string Foreground { get; set; } = "#000000";
public bool ShowBrand { get; set; } = true;
public bool ShowButtonText { get; set; } = true;
public bool ShowButtonIcons { get; set; } = true;
public double Spacing { get; set; } = 8;
public double Padding { get; set; } = 8;


public NavbarItemConfig[] Items { get; set; } =
    Array.Empty<NavbarItemConfig>();


}

public sealed class NavbarItemConfig
{
public string Id { get; set; } = "";
public string Type { get; set; } = "button";
public string Text { get; set; } = "";
public string Icon { get; set; } = "";
public bool Visible { get; set; } = true;
public bool Enabled { get; set; } = true;
}

public sealed class LayoutConfig
{
public string DefaultSplit { get; set; } = "Vertical";
public double PanelSpacing { get; set; } = 4;
public bool ShowPanelBorders { get; set; } = true;
public bool ShowActivePanelBorder { get; set; } = true;
public double ActivePanelBorderThickness { get; set; } = 2;
}

public sealed class BehaviorConfig
{
public bool ConfirmBeforeClosing { get; set; } = false;
public bool RememberWorkingDirectory { get; set; } = true;
public bool RestorePreviousSession { get; set; } = false;
}

public sealed class KeybindingsConfig
{
public string Split { get; set; } = "Ctrl+T";
public string ClosePanel { get; set; } = "Ctrl+W";
public string NextPanel { get; set; } = "Ctrl+Tab";
public string PreviousPanel { get; set; } = "Ctrl+Shift+Tab";
}

public sealed class ShellSelectionConfig
{
    public string Selected { get; set; } = "Command Prompt";
}
public static class ConfigService
{
private static readonly JsonSerializerOptions JsonOptions = new()
{
PropertyNameCaseInsensitive = true,
ReadCommentHandling = JsonCommentHandling.Skip,
AllowTrailingCommas = true,
WriteIndented = true
};


public static string ConfigDirectory =>
    Path.Combine(Environment.CurrentDirectory, "Config");

public static string ConfigPath =>
    Path.Combine(ConfigDirectory, "config.json");

public static AppConfig Load()
{
    try
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        string json = File.ReadAllText(ConfigPath);

        return JsonSerializer.Deserialize<AppConfig>(
            json, JsonOptions) ?? new AppConfig();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"Failed to load config: {ex.Message}");

        return new AppConfig();
    }
}

public static void Save(AppConfig config)
{
    Directory.CreateDirectory(ConfigDirectory);

    string json = JsonSerializer.Serialize(
        config, JsonOptions);

    File.WriteAllText(ConfigPath, json);
}

}