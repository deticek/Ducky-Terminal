using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Exclr8.Terminal;
using Exclr8.Terminal.Buffer;
using Exclr8.Terminal.Pty;
using Exclr8.Terminal.Render;
using Porta.Pty;
using terminal.Commands;

namespace terminal;

public partial class MainWindow : Window
{
    private sealed class TerminalPane{

        public TerminalControl Terminal { get; }
        public PtyTerminalAdapter Adapter { get; }
        public bool ShellStarted { get; set; }
        public string CurrentDirectory { get; set; }
        public string ShellName { get; }
        public StringBuilder CommandLine { get; } = new();
        public int CommandCursor { get; set; }
        public bool CommandLineTrackingReliable { get; set; } = true;

        public TerminalPane(
            TerminalControl terminal,
            PtyTerminalAdapter adapter,
            string currentDirectory,
            string shellName){
            Terminal = terminal;
            Adapter = adapter;
            CurrentDirectory = currentDirectory;
            ShellName = shellName;
        }
    }

    private FileSystemWatcher? _themeWatcher;
    private Avalonia.Threading.DispatcherTimer? _themeReloadTimer;

private readonly List<TerminalPane>[] _workspacePanes =
    Enumerable.Range(0, 10)
        .Select(_ => new List<TerminalPane>())
        .ToArray();

private List<TerminalPane> _panes = new();

private int _activePaneIndex = 0;
private int _activeWorkspaceIndex = 0;

private const int MaxPanes = 4;
private const int MaxWorkspaces = 10;

    private bool _closing;
    private bool _shellRestarting;
    private bool _startupAuthenticated;
    private readonly AppConfig _config;
    private ThemeFile _theme;

    public MainWindow(){
        
        InitializeComponent();

        _startupAuthenticated = !Password.HasPassword();
        TerminalHost.IsVisible = _startupAuthenticated;

        _config = ConfigService.Load();

        _activeWorkspaceIndex = Math.Clamp(
    _config.ActiveWorkspace, 0, MaxWorkspaces - 1);

    _panes = _workspacePanes[_activeWorkspaceIndex];

        _theme = ThemeService.Load(_config.Appearance.Theme);

        ApplyTheme();

        StartThemeWatcher();

        ShellSelector.ItemsSource =
        _shellChoices.Select(choice => choice.Name).ToArray();

        if (!_shellChoices.Any(choice => choice.Name == _config.Shell.Selected))
        {
            _config.Shell.Selected = _shellChoices[0].Name;
        }

        var activeWorkspaceConfig = _config.Workspace.GetSpace(_activeWorkspaceIndex);
        _config.Shell.Selected = GetWorkspaceShell(_activeWorkspaceIndex);
        activeWorkspaceConfig.Shell = _config.Shell.Selected;

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

        //AddPane();

        LoadWorkspacePanes(_activeWorkspaceIndex);
        RebuildLayout();

        Title=$"Ducky - Workspace{_activeWorkspaceIndex}";

        // Sprejemaj bližnjice še pred obdelavo dogodkov terminala.
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        Closed += OnWindowClosed;

        Opened += async (_, _) => {
            if (!_startupAuthenticated)
            {
                bool authenticated = false;
                bool previousAttemptFailed = false;
                while (!authenticated)
                {
                    string? enteredPassword = await RequestStartupPassword(previousAttemptFailed);
                    if (enteredPassword is null)
                    {
                        Close();
                        return;
                    }

                    authenticated = Password.CheckPassword(enteredPassword);
                    previousAttemptFailed = !authenticated;
                }

                _startupAuthenticated = true;
                TerminalHost.IsVisible = true;
                RebuildLayout();
            }

            if (_panes.Count > 0)
                _panes[_activePaneIndex].Terminal.Focus();
        };
    }

    private string GetDefaultDirectory()
{
    return Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile);
}

private void UpdatePaneDirectory(TerminalPane pane, string directory)
{
    if (string.IsNullOrWhiteSpace(directory))
        return;

    if (!Directory.Exists(directory))
        return;

    pane.CurrentDirectory = directory;

    int workspaceIndex = Array.FindIndex(
        _workspacePanes,
        workspace => workspace.Contains(pane));

    if (workspaceIndex >= 0)
        SaveWorkspacePaths(workspaceIndex);
}

private static string NormalizeWorkingDirectory(string workingDirectory)
{
    // OSC 7 may arrive either as a file URI or as the decoded path,
    // depending on which terminal layer raised the event.
    if (Uri.TryCreate(workingDirectory, UriKind.Absolute, out var uri) &&
        uri.IsFile)
    {
        workingDirectory = uri.LocalPath;
    }
    else if (workingDirectory.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
    {
        workingDirectory = Uri.UnescapeDataString(
            workingDirectory.Substring("file:///".Length));
    }

    // Windows file URIs can be surfaced as /C:/folder by OSC parsers.
    if (workingDirectory.Length >= 3 &&
        workingDirectory[0] == '/' &&
        char.IsLetter(workingDirectory[1]) &&
        workingDirectory[2] == ':')
    {
        workingDirectory = workingDirectory.Substring(1);
    }

    return workingDirectory.Replace('/', '\\');
}

private void SaveWorkspacePaths(int workspaceIndex)
{
    var panes = _workspacePanes[workspaceIndex];

    // An empty in-memory list means this workspace has not been loaded.
    // Preserve its saved paths instead of replacing them with blank values.
    if (panes.Count == 0)
        return;

    var workspace = _config.Workspace.GetSpace(workspaceIndex);

    for (int i = 0; i < MaxPanes; i++)
    {
        string path = "";

        if (i < panes.Count)
        {
            path = panes[i].CurrentDirectory;

            if (string.IsNullOrWhiteSpace(path) ||
                !Directory.Exists(path))
            {
                path = GetDefaultDirectory();
            }
        }

        workspace.SetPanel(i, path);
    }

    _config.ActiveWorkspace = _activeWorkspaceIndex;
    ConfigService.Save(_config);
}

private void LoadWorkspacePanes(int workspaceIndex)
{
    _panes = _workspacePanes[workspaceIndex];

    if (_panes.Count > 0)
        return;

    var workspace = _config.Workspace.GetSpace(workspaceIndex);

    int lastSavedPanel = -1;

    for (int i = 0; i < MaxPanes; i++)
    {
        string path = workspace.GetPanel(i);

        if (!string.IsNullOrWhiteSpace(path))
            lastSavedPanel = i;
    }

    // Če workspace še nima shranjenih poti,
    // ustvarimo en panel v privzeti uporabniški mapi.
    if (lastSavedPanel == -1)
    {
        AddPane(GetDefaultDirectory());
        SaveWorkspacePaths(workspaceIndex);
        return;
    }

    // Obnovimo shranjene panele.
    // Prazne ali neobstoječe poti uporabijo privzeto mapo.
    for (int i = 0; i <= lastSavedPanel; i++)
    {
        string path = workspace.GetPanel(i);

        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
        {
            path = GetDefaultDirectory();
        }

        AddPane(path);
    }

    SaveWorkspacePaths(workspaceIndex);
}

private void SwitchWorkspace(int workspaceIndex)
{
    if (workspaceIndex < 0 || workspaceIndex >= MaxWorkspaces)
        return;

    if (workspaceIndex == _activeWorkspaceIndex)
        return;

    // Shranimo trenutni workspace.
    SaveWorkspacePaths(_activeWorkspaceIndex);

    // Zamenjamo aktivni workspace.
    _activeWorkspaceIndex = workspaceIndex;
    _config.ActiveWorkspace = workspaceIndex;

    var workspaceConfig = _config.Workspace.GetSpace(workspaceIndex);
    _config.Shell.Selected = GetWorkspaceShell(workspaceIndex);
    workspaceConfig.Shell = _config.Shell.Selected;
    ShellSelector.SelectedItem = _config.Shell.Selected;

    _panes = _workspacePanes[workspaceIndex];

    // Če še nima aktivnih terminalov, jih obnovimo iz konfiguracije.
    if (_panes.Count == 0)
        LoadWorkspacePanes(workspaceIndex);

    _activePaneIndex = Math.Clamp(
        _activePaneIndex, 0, Math.Max(0, _panes.Count - 1));

    Title = $"Ducky - Workspace {_activeWorkspaceIndex}";

    ConfigService.Save(_config);

    RebuildLayout();

    if (_panes.Count > 0)
        _panes[_activePaneIndex].Terminal.Focus();
}

private string GetWorkspaceShell(int workspaceIndex)
{
    string savedShell = _config.Workspace.GetSpace(workspaceIndex).Shell;

    if (_shellChoices.Any(choice => choice.Name == savedShell))
        return savedShell;

    if (_shellChoices.Any(choice => choice.Name == _config.Shell.Selected))
        return _config.Shell.Selected;

    return _shellChoices[0].Name;
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
        _config.Workspace.GetSpace(_activeWorkspaceIndex).Shell = name;
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

           TerminalPane newPane = AddPane(oldPane.CurrentDirectory);

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
  
    private TerminalPane AddPane(string? startDirectory = null)
    {

        string home = Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile);

        string workingDirectory = !string.IsNullOrWhiteSpace(startDirectory)
        && Directory.Exists(startDirectory)
            ? startDirectory
            : home;

        var terminal = new TerminalControl
        {
            FontFamily = _config.Appearance.FontFamily,
            FontSize = _config.Appearance.FontSize,
            ShowHyperlinkUnderline = false,
            ScrollbackLimit = 10000,
            ColorScheme = CreateTerminalTheme()
        };

        // Exclr8.Terminal renders ANSI underlines as horizontal strokes along
        // cell bottoms. In this app they appear as a distracting grid in
        // full-screen TUIs, so suppress SGR underlines in the visible buffer.
        terminal.Buffer.Changed += (_, _) =>
        {
            for (int rowIndex = 0; rowIndex < terminal.Buffer.Rows; rowIndex++)
            {
                TerminalCell[] row = terminal.Buffer.GetVisibleRow(rowIndex);
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    TerminalCell cell = row[columnIndex];
                    if ((cell.Flags & CellFlags.Underline) == 0)
                        continue;

                    cell.Flags &= ~CellFlags.Underline;
                    row[columnIndex] = cell;
                }
            }
        };

        var selectedShell = _shellChoices.First(choice => choice.Name == _config.Shell.Selected);
        var adapter = new PtyTerminalAdapter(terminal);

        var pane = new TerminalPane(
            terminal,
            adapter,
            workingDirectory,
            selectedShell.Name);

        terminal.Input += (_, input) => TrackCommandLineInput(pane, input.Span);

    terminal.WorkingDirectoryChanged += (_, _) =>
{
    var directory = terminal.WorkingDirectory;

    if (string.IsNullOrWhiteSpace(directory))
        return;

    directory = NormalizeWorkingDirectory(directory);

    if (!Directory.Exists(directory))
        return;

    UpdatePaneDirectory(pane, directory);
};

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
            if (!_startupAuthenticated ||
                pane.ShellStarted ||
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

                var shellEnvironment = new Dictionary<string, string>
                {
                    ["TERM"] = "xterm-256color",
                    ["COLORTERM"] = "truecolor"
                };

                string customCommandDirectory = ConfigService.CommandsDirectory;
                Directory.CreateDirectory(customCommandDirectory);

                string existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                shellEnvironment["PATH"] = string.IsNullOrWhiteSpace(existingPath)
                    ? customCommandDirectory
                    : customCommandDirectory + Path.PathSeparator + existingPath;

                if (selectedShell.Name == "Command Prompt")
                {
                    // The visible CMD prompt is configurable; OSC 7 remains
                    // attached so current-directory tracking keeps working.
                    string promptFormat = string.IsNullOrWhiteSpace(_config.Shell.Prompt)
                        ? new ShellSelectionConfig().Prompt
                        : _config.Shell.Prompt;

                    string visiblePrompt = promptFormat
                        .Replace("{username}", Environment.UserName, StringComparison.OrdinalIgnoreCase)
                        .Replace("{computer}", Environment.MachineName, StringComparison.OrdinalIgnoreCase);

                    shellEnvironment["PROMPT"] =
                        @"$E]7;file:///$P$E\" + visiblePrompt;
                }

                await adapter.StartAsync(new PtyOptions
                {
                    Name = "xterm-256color",
                    App = selectedShell.App,
                    CommandLine = selectedShell.Arguments,
                    Cwd = pane.CurrentDirectory,
                    Cols = size.Cols,
                    Rows = size.Rows,
                    Environment = shellEnvironment
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
    if (_panes.Count >= MaxPanes)
        return;

    string currentDirectory = GetCurrentPanelPath();

    var pane = AddPane(currentDirectory);

    _activePaneIndex = _panes.IndexOf(pane);

    RebuildLayout();

    SaveWorkspacePaths(_activeWorkspaceIndex);

    pane.Terminal.Focus();
}

private string GetCurrentPanelPath()
{
    if (_panes.Count == 0)
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    int index = Math.Clamp(_activePaneIndex, 0, _panes.Count - 1);

    return _panes[index].CurrentDirectory;
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

        _activePaneIndex = Math.Min(index, _panes.Count -1);

        SaveWorkspacePaths(_activeWorkspaceIndex);

        await pane.Adapter.DisposeAsync();

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

    private bool TryGetWorkspaceNumber(Key key, out int workspaceIndex)
{
    workspaceIndex = key switch
    {
        Key.D0 or Key.NumPad0 => 0,
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => -1
    };

    return workspaceIndex >= 0;
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

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
    TryGetWorkspaceNumber(e.Key, out int workspaceIndex))
    {
        SwitchWorkspace(workspaceIndex);
        e.Handled = true;
        return;
    }

        bool altGrOrCtrlAlt =
        e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
        e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (OperatingSystem.IsWindows() && altGrOrCtrlAlt)
        {
        return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)){
            
            if (e.Key == Key.F4)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemPlus ||e.Key == Key.Add)
            {
                SwitchWorkspace((_activeWorkspaceIndex + 1) % MaxWorkspaces);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemMinus ||e.Key == Key.Subtract)
            {
                SwitchWorkspace(
                    (_activeWorkspaceIndex + MaxWorkspaces - 1) % MaxWorkspaces);
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
            if (e.Key == Key.Back)
            {
                if (_panes.Count > 0)
                {
                    int index = Math.Clamp(_activePaneIndex, 0, _panes.Count - 1);
                    var pane = _panes[index];

                    if (pane.ShellStarted && pane.Adapter.IsAlive)
                    {
                        if (pane.ShellName == "Command Prompt")
                            DeletePreviousCommandWord(pane);
                        else
                            pane.Adapter.Send(System.Text.Encoding.UTF8.GetBytes("\x17"));
                    }
                }

                e.Handled = true;
                return;
            }

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

                case Key.R:
                    await SetActivePaneDirectoryToHomeAsync();
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

    private static void TrackCommandLineInput(TerminalPane pane, ReadOnlySpan<byte> input)
    {
        if (pane.ShellName != "Command Prompt")
            return;

        string text = Encoding.UTF8.GetString(input);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c is '\r' or '\n')
            {
                pane.CommandLine.Clear();
                pane.CommandCursor = 0;
                pane.CommandLineTrackingReliable = true;
                continue;
            }

            if (c is '\b' or '\x7f')
            {
                if (pane.CommandCursor > 0)
                {
                    pane.CommandLine.Remove(--pane.CommandCursor, 1);
                }
                continue;
            }

            if (c == '\x1b')
            {
                string[] leftSequences = ["\x1b[D", "\x1b[1D", "\x1b[H", "\x1b[1~"];
                string[] rightSequences = ["\x1b[C", "\x1b[1C", "\x1b[F", "\x1b[4~"];
                string[] deleteSequences = ["\x1b[3~"];
                string? sequence = new[] { leftSequences, rightSequences, deleteSequences }
                    .SelectMany(group => group)
                    .Where(candidate => text.AsSpan(i).StartsWith(candidate, StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.Length)
                    .FirstOrDefault();

                if (sequence is null)
                {
                    pane.CommandLineTrackingReliable = false;
                    continue;
                }

                if (leftSequences.Contains(sequence, StringComparer.Ordinal))
                    pane.CommandCursor = Math.Max(0, pane.CommandCursor - 1);
                else if (rightSequences.Contains(sequence, StringComparer.Ordinal))
                    pane.CommandCursor = Math.Min(pane.CommandLine.Length, pane.CommandCursor + 1);
                else if (pane.CommandCursor < pane.CommandLine.Length)
                    pane.CommandLine.Remove(pane.CommandCursor, 1);

                i += sequence.Length - 1;
                continue;
            }

            if (char.IsControl(c))
            {
                switch (c)
                {
                    case '\x01': // Ctrl+A
                        pane.CommandCursor = 0;
                        break;
                    case '\x05': // Ctrl+E
                        pane.CommandCursor = pane.CommandLine.Length;
                        break;
                    case '\x03': // Ctrl+C
                    case '\x15': // Ctrl+U
                        pane.CommandLine.Clear();
                        pane.CommandCursor = 0;
                        break;
                    default:
                        pane.CommandLineTrackingReliable = false;
                        break;
                }
                continue;
            }

            pane.CommandLine.Insert(pane.CommandCursor++, c);
        }
    }

    private static void DeletePreviousCommandWord(TerminalPane pane)
    {
        if (!pane.CommandLineTrackingReliable || pane.CommandCursor == 0)
            return;

        int start = pane.CommandCursor;
        while (start > 0 && char.IsWhiteSpace(pane.CommandLine[start - 1]))
            start--;
        while (start > 0 && !char.IsWhiteSpace(pane.CommandLine[start - 1]))
            start--;

        int count = pane.CommandCursor - start;
        if (count == 0)
            return;

        byte[] backspaces = Enumerable.Repeat((byte)0x7f, count).ToArray();
        if (pane.Adapter.Send(backspaces))
        {
            pane.CommandLine.Remove(start, count);
            pane.CommandCursor = start;
        }
    }

    private async Task SetActivePaneDirectoryToHomeAsync()
    {
        if (_panes.Count == 0)
            return;

        int index = Math.Clamp(_activePaneIndex, 0, _panes.Count - 1);
        var pane = _panes[index];
        string home = GetDefaultDirectory();

        if (!Directory.Exists(home))
            return;

        // If the terminal has not started yet, its next PTY will use this CWD.
        if (!pane.ShellStarted)
        {
            UpdatePaneDirectory(pane, home);
            return;
        }

        // The resize handler may still be starting the PTY.
        for (int attempt = 0; attempt < 40 && !pane.Adapter.IsAlive; attempt++)
            await Task.Delay(25);

        if (!pane.Adapter.IsAlive)
            return;

        string command = pane.ShellName switch
        {
            "Command Prompt" => $"cd /d \"{home}\"\r",
            "Windows PowerShell" or "PowerShell 7" =>
                $"Set-Location -LiteralPath {QuotePowerShell(home)}\r",
            _ => $"cd -- {QuoteBash(home)}\n"
        };

        if (pane.Adapter.Send(System.Text.Encoding.UTF8.GetBytes(command)))
            UpdatePaneDirectory(pane, home);
    }

    private static string QuotePowerShell(string path) =>
        "'" + path.Replace("'", "''") + "'";

    private static string QuoteBash(string path) =>
        "'" + path.Replace("'", "'\\''") + "'";

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
    if (_closing)
        return;

    _closing = true;

    // Shrani poti vseh workspaceov.
    for (int i = 0; i < MaxWorkspaces; i++)
    {
        SaveWorkspacePaths(i);
    }

    _config.ActiveWorkspace = _activeWorkspaceIndex;
    ConfigService.Save(_config);

    // Zapri terminale v vseh workspaceih.
    foreach (var workspace in _workspacePanes)
    {
        foreach (var pane in workspace.ToArray())
        {
            await pane.Adapter.DisposeAsync();
        }

        workspace.Clear();
    }

    _panes.Clear();

    _themeWatcher?.Dispose();
    _themeReloadTimer?.Stop();
}

    private void OnShowShortcutsClicked( object? sender, RoutedEventArgs e)
    {
        ShowShortcutsHelp();
    }

    private async Task<string?> RequestStartupPassword(bool previousAttemptFailed)
    {
        var statusText = new TextBlock
        {
            Text = previousAttemptFailed
                ? "Incorrect password. Try again."
                : "Enter your password to continue.",
            Foreground = previousAttemptFailed ? Brushes.IndianRed : Foreground
        };
        var passwordBox = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = "Password",
            MinWidth = 260,
            Margin = new Thickness(0, 10, 0, 14)
        };

        var dialog = new Window
        {
            Title = "Ducky password",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background,
            Foreground = Foreground,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 8,
                Children =
                {
                    statusText,
                    passwordBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Cancel", MinWidth = 80, Name = "CancelPasswordButton" },
                            new Button { Content = "Unlock", MinWidth = 80, Name = "UnlockPasswordButton" }
                        }
                    }
                }
            }
        };

        var buttons = (StackPanel)((StackPanel)dialog.Content!).Children[2];
        ((Button)buttons.Children[0]).Click += (_, _) => dialog.Close(null);
        ((Button)buttons.Children[1]).Click += (_, _) => dialog.Close(passwordBox.Text ?? string.Empty);
        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                dialog.Close(passwordBox.Text ?? string.Empty);
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => passwordBox.Focus();

        return await dialog.ShowDialog<string?>(this);
    }

    private void ShowShortcutsHelp()
{
    var helpWindow = new Window
    {
        Title = "Keyboard shortcuts",
        SizeToContent = SizeToContent.Height,
        Width = 400,
        MinWidth = 400,
        MinHeight = 600,
        MaxHeight = 800,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = this.Background,
        Foreground = this.Foreground,
        CanResize = false,
        Content = new StackPanel
        {
            Margin = new Thickness(20,20,20,10),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "KEYBOARD SHORTCUTS",
                    FontSize = 20,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Foreground = this.Foreground,
                    Margin = new Thickness(0, 0, 0, 8)
                },

                new TextBlock
                {
                    Text =
                        "══════════════════ TERMINAL ══════════════════\n\n"+
                        "F1                         Show this help\n" +
                        "Alt + T                    Open theme list\n" +
                        "Alt + S                    Open shell list\n" +
                        "Alt + F                    Open font list\n" +
                        "Ctrl + +                   Increase font size\n" +
                        "Ctrl + -                   Decrease font size\n" +
                        "Ctrl + R                   Reset path to home\n" +
                        "Alt + F4                   Exit \n\n" +
                        "════════════════════ PANE ════════════════════\n\n"+
                        "Ctrl + T                   Split pane\n" +
                        "Ctrl + W                   Close active pane\n" +
                        "Ctrl + R                   Go to home directory\n" +
                        "Ctrl + Tab                 Next pane\n" +
                        "Ctrl + Shift + Tab         Previous pane\n" +
                        "\n" +
                        "═════════════════ WORKSPACES ═════════════════\n\n"+
                        "Alt + 0                    Move in workspace 0\n" +
                        "Alt + 1                    Move in workspace 1\n" +
                        "Alt + 2                    Move in workspace 2\n" +
                        "Alt + 3                    Move in workspace 3\n" +
                        "Alt + 4                    Move in workspace 4\n" +
                        "Alt + 5                    Move in workspace 5\n" +
                        "Alt + 6                    Move in workspace 6\n" +
                        "Alt + 7                    Move in workspace 7\n" +
                        "Alt + 8                    Move in workspace 8\n" +
                        "Alt + 9                    Move in workspace 9\n" +
                        "Alt + Plus                 Next workspace\n" +
                        "Alt + Minus                Previous workspace\n",

                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    Foreground = this.Foreground
                },

                new Button
                {
                    Content = "Close",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Padding = new Thickness(20, 8),
                }
            }
        }
    };

    helpWindow.KeyDown += (_, e) =>
    {
        if (e.Key == Key.Escape || e.Key == Key.F1 ||
            (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            helpWindow.Close();
            e.Handled = true;
        }
    };

    if (helpWindow.Content is StackPanel panel &&
        panel.Children[2] is Button closeButton)
    {
        closeButton.Click += (_, _) => helpWindow.Close();
    }

    helpWindow.ShowDialog(this);
}

}
