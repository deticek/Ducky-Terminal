using System;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace terminal;

public sealed class ThemeFile
{
    public string Name { get; set; } = "Windows XP Retro";
    public string Author { get; set; } = "Dterminal";
    public ThemeColors Colors { get; set; } = new();
    public TerminalColors Terminal { get; set; } = new();
}

public sealed class TerminalColors
{
    public string Black { get; set; } = "#000000";
    public string Red { get; set; } = "#800000";
    public string Green { get; set; } = "#008000";
    public string Yellow { get; set; } = "#808000";
    public string Blue { get; set; } = "#000080";
    public string Magenta { get; set; } = "#800080";
    public string Cyan { get; set; } = "#008080";
    public string White { get; set; } = "#C0C0C0";
    public string BrightBlack { get; set; } = "#808080";
    public string BrightRed { get; set; } = "#FF0000";
    public string BrightGreen { get; set; } = "#00FF00";
    public string BrightYellow { get; set; } = "#FFFF00";
    public string BrightBlue { get; set; } = "#0000FF";
    public string BrightMagenta { get; set; } = "#FF00FF";
    public string BrightCyan { get; set; } = "#00FFFF";
    public string BrightWhite { get; set; } = "#FFFFFF";
}

public sealed class ThemeColors
{
    public string WindowBackground { get; set; } = "#ECE9D8";
    public string PanelBackground { get; set; } = "#000000";
    public string Foreground { get; set; } = "#C0C0C0";
    public string Cursor { get; set; } = "#FFFFFF";
    public string SelectionBackground { get; set; } = "#000080";
    public string SelectionForeground { get; set; } = "#FFFFFF";
    public string PanelBorder { get; set; } = "#7F9DB9";
    public string ToolbarBackground { get; set; } = "#D4D0C8";
    public string ToolbarForeground { get; set; } = "#000000";
    public string Accent { get; set; } = "#316AC5";
}

public static class ThemeService
{

public static string[] GetAvailableThemeNames()
{
    string themesDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Themes");

    if (!Directory.Exists(themesDirectory))
        return Array.Empty<string>();

    return Directory.GetFiles(themesDirectory, "*.json")
        .Select(path => Path.GetFileNameWithoutExtension(path)!)
        .OrderBy(name => name)
        .ToArray();
}

    public static ThemeFile Load(string themeName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Themes",
            $"{themeName}.json");

        try
        {
            if (!File.Exists(path))
                return new ThemeFile();

            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<ThemeFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ThemeFile();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to load theme: {ex.Message}");

            return new ThemeFile();
        }
    }
}