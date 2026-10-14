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
public WorkspaceConfig Workspace { get; set; } = new();
public int ActiveWorkspace { get; set; } = 0;

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

public sealed class WorkspaceConfig
{
    public WorkspaceSpaceConfig Space0 { get; set; } = new();
    public WorkspaceSpaceConfig Space1 { get; set; } = new();
    public WorkspaceSpaceConfig Space2 { get; set; } = new();
    public WorkspaceSpaceConfig Space3 { get; set; } = new();
    public WorkspaceSpaceConfig Space4 { get; set; } = new();
    public WorkspaceSpaceConfig Space5 { get; set; } = new();
    public WorkspaceSpaceConfig Space6 { get; set; } = new();
    public WorkspaceSpaceConfig Space7 { get; set; } = new();
    public WorkspaceSpaceConfig Space8 { get; set; } = new();
    public WorkspaceSpaceConfig Space9 { get; set; } = new();

    public WorkspaceSpaceConfig GetSpace(int index)
    {
        return index switch
        {
            0 => Space0,
            1 => Space1,
            2 => Space2,
            3 => Space3,
            4 => Space4,
            5 => Space5,
            6 => Space6,
            7 => Space7,
            8 => Space8,
            9 => Space9,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}

public sealed class WorkspaceSpaceConfig
{
    public string Panel1 { get; set; } = "";
    public string Panel2 { get; set; } = "";
    public string Panel3 { get; set; } = "";
    public string Panel4 { get; set; } = "";
    public string Shell { get; set; } = "";

    public string GetPanel(int index)
    {
        return index switch
        {
            0 => Panel1,
            1 => Panel2,
            2 => Panel3,
            3 => Panel4,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    public void SetPanel(int index, string path)
    {
        switch (index)
        {
            case 0: Panel1 = path; break;
            case 1: Panel2 = path; break;
            case 2: Panel3 = path; break;
            case 3: Panel4 = path; break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
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
