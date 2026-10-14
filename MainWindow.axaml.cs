using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Exclr8.Terminal;
using Exclr8.Terminal.Pty;
using Exclr8.Terminal.Render;
using Porta.Pty;

namespace terminal;

public partial class MainWindow : Window
{
    private sealed class TerminalPane{

        public TerminalControl Terminal { get; }
        public PtyTerminalAdapter Adapter { get; }
        public bool ShellStarted { get; set; }

        public TerminalPane(TerminalControl terminal, PtyTerminalAdapter adapter){
            Terminal = terminal;
            Adapter = adapter;
        }
    }

    private FileSystemWatcher? _themeWatcher;
    private Avalonia.Threading.DispatcherTimer? _themeReloadTimer;

    private readonly List<TerminalPane> _panes = new();

    private int _activePaneIndex = 0;
    private bool _closing;
    private bool _shellRestarting;
    private readonly AppConfig _config;
    private ThemeFile _theme;

    public MainWindow(){
        
        InitializeComponent();

        _config = ConfigService.Load();
        _theme = ThemeService.Load(_config.Appearance.Theme);

        ApplyTheme();

        StartThemeWatcher();

        ShellSelector.ItemsSource =
        _shellChoices.Select(choice => choice.Name).ToArray();

        if (!_shellChoices.Any(choice => choice.Name == _config.Shell.Selected))
        {
            _config.Shell.Selected = _shellChoices[0].Name;
        }

        ShellSelector.SelectedItem = _config.Shell.Selected;

        var installedFonts = FontManager.Current.SystemFonts
        .Select(font => font.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var fontChoices = new[]
        {
            "Consolas",
            "Cascadia Mono",
            "Cascadia Code",
            "Courier New",
            "Lucida Console",
            "JetBrains Mono",
            "Fira Code",
            "Source Code Pro",
            "Menlo",
            "DejaVu Sans Mono",
            "Liberation Mono"
        }
        .Where(installedFonts.Contains)
        .ToList();
        
        if (!fontChoices.Contains(
                _config.Appearance.FontFamily,
                StringComparer.OrdinalIgnoreCase))
        {
            fontChoices.Insert(0, _config.Appearance.FontFamily);
        }
        
        FontSelector.ItemsSource = fontChoices;
        FontSelector.SelectedItem = _config.Appearance.FontFamily;

        AddPane();
        RebuildLayout();

        // Sprejemaj bližnjice še pred obdelavo dogodkov terminala.
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        Closed += OnWindowClosed;

        Opened += (_, _) => {
            if (_panes.Count > 0)
                _panes[_activePaneIndex].Terminal.Focus();
        };
    }

    private void StartThemeWatcher()
{
    string directory = ThemeService.ThemesDirectory;

    _themeReloadTimer = new Avalonia.Threading.DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };

    _themeReloadTimer.Tick += (_, _) =>
    {
        _themeReloadTimer.Stop();

        try
        {
            _theme = ThemeService.Load(_config.Appearance.Theme);
            ApplyTheme();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to reload theme: {ex.Message}");
        }
    };

    _themeWatcher = new FileSystemWatcher(
    ThemeService.ThemesDirectory,
    "*.json")
{
    NotifyFilter = NotifyFilters.LastWrite |
                   NotifyFilters.FileName,
    EnableRaisingEvents = true
};

    _themeWatcher.Changed += OnThemeFileChanged;
    _themeWatcher.Created += OnThemeFileChanged;
    _themeWatcher.Renamed += OnThemeFileRenamed;

    _themeWatcher.EnableRaisingEvents = true;

}

private void OnThemeFileRenamed(
    object sender,
    RenamedEventArgs e)
{
    QueueThemeReload();
}

private void OnThemeFileChanged(
    object? sender,
    FileSystemEventArgs e)
{
    QueueThemeReload();
}

private void QueueThemeReload()
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        _themeReloadTimer?.Stop();
        _themeReloadTimer?.Start();
    });
}

    private void OnFontSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FontSelector.SelectedItem is not string fontName ||
            fontName == _config.Appearance.FontFamily)
        {
            return;
        }

        _config.Appearance.FontFamily = fontName;

        foreach (var pane in _panes)
        {
            pane.Terminal.FontFamily = fontName;
        }

        ConfigService.Save(_config);
    }

    private void OnThemeSelectionChanged(object? sender,SelectionChangedEventArgs e){

        if (ThemeSelector.SelectedItem is not string themeName ||
            themeName == _config.Appearance.Theme)
        {
            return;
        }

        _theme = ThemeService.Load(themeName);
        _config.Appearance.Theme = themeName;

        ConfigService.Save(_config);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        Background = Avalonia.Media.Brushes.Transparent;

        Background = new SolidColorBrush(Color.Parse(_theme.Colors.WindowBackground));

        NavbarBorder.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        BrandText.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        SplitButton.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        SplitButton.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        SplitButton.BorderBrush = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBorder));

        //help button
        HelpButton.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        HelpButton.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        HelpButton.BorderBrush = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBorder));

        //theme selector
        ThemeSelector.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        ThemeSelector.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        ThemeSelector.BorderBrush = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBorder));

        //shell selector
        ShellSelector.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        ShellSelector.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        ShellSelector.BorderBrush = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBorder));

        //Font Selector
        FontSelector.Background = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarBackground));

        FontSelector.Foreground = new SolidColorBrush(Color.Parse(_theme.Colors.ToolbarForeground));

        FontSelector.BorderBrush = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBorder));

        TerminalHost.Background = new SolidColorBrush(Color.Parse(_theme.Colors.PanelBackground));

        NavbarBorder.Height = _config.Navbar.Height;
        NavbarBorder.Padding = new Thickness(_config.Navbar.Padding);

        NavbarBorder.IsVisible =_config.Window.ShowNavbar &&_config.Navbar.Visible;

        BrandText.IsVisible = _config.Navbar.ShowBrand;

        Opacity = Math.Clamp(_config.Appearance.WindowOpacity, 0.2, 1.0);

        ThemeSelector.ItemsSource = ThemeService.GetAvailableThemeNames();
        ThemeSelector.SelectedItem = _config.Appearance.Theme;

        foreach (var pane in _panes){
            pane.Terminal.ColorScheme = CreateTerminalTheme();
        }
    }

    private TerminalTheme CreateTerminalTheme()
    {
        return new TerminalTheme
        {
            Background = Color.Parse(_theme.Colors.PanelBackground),
            Foreground = Color.Parse(_theme.Colors.Foreground),
            Cursor = Color.Parse(_theme.Colors.Cursor),
            AnsiColors = new Color?[]
            {
                Color.Parse(_theme.Terminal.Black),
                Color.Parse(_theme.Terminal.Red),
                Color.Parse(_theme.Terminal.Green),
                Color.Parse(_theme.Terminal.Yellow),
                Color.Parse(_theme.Terminal.Blue),
                Color.Parse(_theme.Terminal.Magenta),
                Color.Parse(_theme.Terminal.Cyan),
                Color.Parse(_theme.Terminal.White),
                Color.Parse(_theme.Terminal.BrightBlack),
                Color.Parse(_theme.Terminal.BrightRed),
                Color.Parse(_theme.Terminal.BrightGreen),
                Color.Parse(_theme.Terminal.BrightYellow),
                Color.Parse(_theme.Terminal.BrightBlue),
                Color.Parse(_theme.Terminal.BrightMagenta),
                Color.Parse(_theme.Terminal.BrightCyan),
                Color.Parse(_theme.Terminal.BrightWhite)
            }
        };
    }

    private sealed record ShellChoice(string Name,string App,string[] Arguments);

    private readonly List<ShellChoice> _shellChoices =CreateShellChoices();

    private static List<ShellChoice> CreateShellChoices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new List<ShellChoice>
            {
                new("Bash", "/bin/bash", Array.Empty<string>())
            };
        }

        var choices = new List<ShellChoice>
        {
            new(
                "Command Prompt",
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Array.Empty<string>()),

            new(
                "Windows PowerShell",
                Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                new[] { "-NoLogo" })
        };

        string? pwsh = FindOnPath("pwsh.exe");

        if (pwsh is not null)
        {
            choices.Add(new("PowerShell 7", pwsh, new[] { "-NoLogo" }));
        }

        return choices;
    }

    private static string? FindOnPath(string executable)
    {
        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (string directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), executable);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }


    private async void OnShellSelectionChanged(object? sender,SelectionChangedEventArgs e)
    {
        if (ShellSelector.SelectedItem is not string name ||
            name == _config.Shell.Selected ||
            _shellRestarting)
        {
            return;
        }

        _config.Shell.Selected = name;
        ConfigService.Save(_config);

        await RestartActivePaneShellAsync();
    }

    private async Task RestartActivePaneShellAsync()
    {
        if (_panes.Count == 0)return;

        _shellRestarting = true;

        int index = Math.Clamp(
            _activePaneIndex,
            0,
            _panes.Count - 1);

        TerminalPane oldPane = _panes[index];
        _panes.RemoveAt(index);

        try
        {
            await oldPane.Adapter.DisposeAsync();

            TerminalPane newPane = AddPane();

            _panes.Remove(newPane);
            _panes.Insert(index, newPane);

            _activePaneIndex = index;

            RebuildLayout();

            newPane.Terminal.Focus();
        }
        finally
        {
            _shellRestarting = false;
        }
    }

    /*

    ADD PANE FUNCTION

    // Ustvari nov terminalski panel.*/
  
    private TerminalPane AddPane()
    {
        var terminal = new TerminalControl
        {
            FontFamily = _config.Appearance.FontFamily,
            FontSize = _config.Appearance.FontSize,
            ScrollbackLimit = 10000,
            ColorScheme = CreateTerminalTheme()
        };

        var selectedShell = _shellChoices.First(choice => choice.Name == _config.Shell.Selected);
        var adapter = new PtyTerminalAdapter(terminal);

        var pane = new TerminalPane(terminal, adapter);

        _panes.Add(pane);

        adapter.ProcessExited += async (_, _) =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                int index = _panes.IndexOf(pane);

                // Panel je bil morda že zaprt.
                if (index < 0)
                    return;

                // Če je to zadnji panel, zapri program.
                if (_panes.Count == 1)
                {
                    Close();
                    return;
                }

                _panes.RemoveAt(index);

                _activePaneIndex = Math.Min(
                    _activePaneIndex,
                    _panes.Count - 1);

                await pane.Adapter.DisposeAsync();

                RebuildLayout();

                _panes[_activePaneIndex].Terminal.Focus();
            });
        };

        terminal.GotFocus += (_, _) =>
        {
            int index = _panes.IndexOf(pane);

            if (index >= 0)
                _activePaneIndex = index;
        };

        terminal.Resized += async (_, size) =>
        {
            if (pane.ShellStarted ||
                size.Cols <= 0 ||
                size.Rows <= 0)
            {
                return;
            }

            pane.ShellStarted = true;

            try
            {
                string shell;

                string home = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

                if (OperatingSystem.IsWindows())
                {
                    shell = Environment.GetEnvironmentVariable("COMSPEC")
                        ?? "cmd.exe";
                }
                else if (OperatingSystem.IsMacOS())
                {
                    shell = "/bin/zsh";
                }
                else
                {
                    shell = "/bin/bash";
                }

                await adapter.StartAsync(new PtyOptions
                {
                    Name = "xterm-256color",
                    App = selectedShell.App,
                    CommandLine = selectedShell.Arguments,
                    Cwd = home,
                    Cols = size.Cols,
                    Rows = size.Rows,

                    Environment = new Dictionary<string, string>
                    {
                        ["TERM"] = "xterm-256color",
                        ["COLORTERM"] = "truecolor"
                    }
                });
            }
            catch (Exception ex)
            {
                pane.ShellStarted = false;

                terminal.Write(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"\r\nFailed to start shell: {ex.Message}\r\n"));
            }
        };

        return pane;
    }

    // Ustvari nov panel in posodobi postavitev.
    private void SplitTerminal()
    {
        var pane = AddPane();

        _activePaneIndex = _panes.Count - 1;

        RebuildLayout();

        pane.Terminal.Focus();
    }

    // Ponovno sestavi mrežo panelov.
    private void RebuildLayout()
    {
        TerminalHost.Children.Clear();
        TerminalHost.ColumnDefinitions.Clear();

        for (int i = 0; i < _panes.Count; i++)
        {
            TerminalHost.ColumnDefinitions.Add(
                new ColumnDefinition(1, GridUnitType.Star));

            Grid.SetColumn(_panes[i].Terminal, i * 2);

            TerminalHost.Children.Add(_panes[i].Terminal);

            if (i < _panes.Count - 1)
            {
                TerminalHost.ColumnDefinitions.Add(
                    new ColumnDefinition(5, GridUnitType.Pixel));

                var splitter = new GridSplitter
                {
                    Width = 5,
                    Background = new SolidColorBrush(
                        Color.Parse("#45475a")),

                    HorizontalAlignment =
                        Avalonia.Layout.HorizontalAlignment.Stretch,

                    VerticalAlignment =
                        Avalonia.Layout.VerticalAlignment.Stretch,

                    ResizeDirection = GridResizeDirection.Columns
                };

                Grid.SetColumn(splitter, i * 2 + 1);

                TerminalHost.Children.Add(splitter);
            }
        }

        if (_panes.Count == 1)
        {
            SplitButton.IsEnabled = true;
        }
    }

    // Zapre trenutno izbrani panel.
    private async Task CloseActivePaneAsync()
    {
        // Vedno ohrani vsaj en panel.
        if (_panes.Count <= 1){
            Close();
            return;
        }

        int index = Math.Clamp(_activePaneIndex, 0, _panes.Count - 1);

        var pane = _panes[index];

        _panes.RemoveAt(index);

        await pane.Adapter.DisposeAsync();

        _activePaneIndex = Math.Min(
            index, _panes.Count - 1);

        RebuildLayout();

        _panes[_activePaneIndex].Terminal.Focus();
    }

    // Preklopi na naslednji panel.
    private void NextPane()
    {
        if (_panes.Count <= 1) return;

        _activePaneIndex = (_activePaneIndex + 1) % _panes.Count;

        _panes[_activePaneIndex].Terminal.Focus();
    }

    // Preklopi na prejšnji panel.
    private void PreviousPane()
    {
        if (_panes.Count <= 1) return;

        _activePaneIndex = (_activePaneIndex - 1 + _panes.Count) % _panes.Count;

        _panes[_activePaneIndex].Terminal.Focus();
    }

    // Obstoječi gumb za razdelitev še naprej deluje.
    private void OnSplitClicked(object? sender,RoutedEventArgs e)
    {
        SplitTerminal();
    }

    private void CloseAllDropDown(){
        ThemeSelector.IsDropDownOpen = false;
        ShellSelector.IsDropDownOpen = false;
        FontSelector.IsDropDownOpen = false;
    }

    // Bližnjice na tipkovnici.
    private async void OnKeyDown(object? sender,KeyEventArgs e){
        
        if (e.Key == Key.Escape && (ThemeSelector.IsDropDownOpen || ShellSelector.IsDropDownOpen))
        {
            ThemeSelector.IsDropDownOpen = false;
            ShellSelector.IsDropDownOpen = false;

            if (_panes.Count > 0)
            {
                int index = Math.Clamp(_activePaneIndex, 0, _panes.Count - 1);
                _panes[index].Terminal.Focus();
            }

            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)){
            
            if (e.Key == Key.F4)
            {
                Close();
                e.Handled = true;
                return;
            }
        
            if (e.Key == Key.T)
            {
                CloseAllDropDown();
                ThemeSelector.Focus();
                ThemeSelector.IsDropDownOpen = true;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                CloseAllDropDown();
                ShellSelector.Focus();
                ShellSelector.IsDropDownOpen = true;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F)
            {
                CloseAllDropDown();
                FontSelector.Focus();
                FontSelector.IsDropDownOpen = true;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F1)
        {
            ShowShortcutsHelp();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.T:
                    SplitTerminal();
                    e.Handled = true;
                    break;

                case Key.W:
                    await CloseActivePaneAsync();
                    e.Handled = true;
                    break;

                case Key.Tab:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        PreviousPane();
                    else
                        NextPane();

                    e.Handled = true;
                    break;
                    case Key.OemPlus:
                case Key.Add:
                    AdjustTerminalFontSize(1);
                    e.Handled = true;
                    break;

                case Key.OemMinus:
                case Key.Subtract:
                    AdjustTerminalFontSize(-1);
                    e.Handled = true;
                    break;
            }
        }
    }

    private void AdjustTerminalFontSize(int change)
    {
        if (_panes.Count == 0) return;

        foreach (var pane in _panes)
        {
            pane.Terminal.AdjustFontSize(change);
        }

        _config.Appearance.FontSize = _panes[0].Terminal.FontSize;
        ConfigService.Save(_config);
    }

    // Ob zaprtju aplikacije zapri vse preostale PTY-povezave.
    private async void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_closing) return;

        _closing = true;

        foreach (var pane in _panes.ToArray())
        {
            await pane.Adapter.DisposeAsync();
        }

        _panes.Clear();
        _themeWatcher?.Dispose();
        _themeReloadTimer?.Stop();
    }

    private void OnShowShortcutsClicked( object? sender, RoutedEventArgs e)
    {
        ShowShortcutsHelp();
    }

    private void ShowShortcutsHelp()
    {
        var helpWindow = new Window
        {
            Title = "Keyboard shortcuts",
            Width = 430,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "Alt+T              Open theme list\n" +
                    "Alt+S              Open shell list\n" +
                    "Alt+F              Open font list\n" +
                    "F1                 Show this help\n" +
                    "Ctrl+T             Split pane\n" +
                    "Ctrl+W             Close active pane\n" +
                    "Ctrl+Tab           Next pane\n" +
                    "Ctrl+Shift+Tab     Previous pane\n" +
                    "Ctrl++             Increase font size\n" +
                    "Ctrl+-             Decrease font size\n" +
                    "Alt+F4             Exit"
            }
        };

        helpWindow.AddHandler(
        InputElement.KeyDownEvent,
        (_, e) =>
        {
            bool closeHelp = e.Key == Key.Escape || e.Key == Key.F1 ||
            (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control));

            if (closeHelp)
            {
                helpWindow.Close();
                e.Handled = true;
            }
        },
        RoutingStrategies.Tunnel);

        helpWindow.ShowDialog(this);
    }

}