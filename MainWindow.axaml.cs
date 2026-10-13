using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

using Exclr8.Terminal;
using Exclr8.Terminal.Pty;
using Exclr8.Terminal.Render;
using Porta.Pty;

namespace terminal;

public partial class MainWindow : Window
{
    private sealed class TerminalPane
    {
        public TerminalControl Terminal { get; }
        public PtyTerminalAdapter Adapter { get; }

        public bool ShellStarted { get; set; }

        public TerminalPane(
            TerminalControl terminal,
            PtyTerminalAdapter adapter)
        {
            Terminal = terminal;
            Adapter = adapter;
        }
    }

    private readonly List<TerminalPane> _panes = new();

    private int _activePaneIndex = 0;
    private bool _closing;
    private readonly AppConfig _config;
    private ThemeFile _theme;

public MainWindow()
{
    InitializeComponent();
    
    _config = ConfigService.Load();
    _theme = ThemeService.Load(_config.Appearance.Theme);

    ApplyTheme();

    AddPane();
    RebuildLayout();

    // Sprejemaj bližnjice še pred obdelavo dogodkov terminala.
    AddHandler(
        InputElement.KeyDownEvent,
        OnKeyDown,
        RoutingStrategies.Tunnel);

    Closed += OnWindowClosed;
    
    Opened += (_, _) =>
{
    if (_panes.Count > 0)
        _panes[_activePaneIndex].Terminal.Focus();
};
}

private void OnThemeSelectionChanged(
    object? sender,
    SelectionChangedEventArgs e)
{
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

    Background = new SolidColorBrush(
        Color.Parse(_theme.Colors.WindowBackground));

    NavbarBorder.Background = new SolidColorBrush(
        Color.Parse(_theme.Colors.ToolbarBackground));

    BrandText.Foreground = new SolidColorBrush(
        Color.Parse(_theme.Colors.ToolbarForeground));

    SplitButton.Background = new SolidColorBrush(
        Color.Parse(_theme.Colors.ToolbarBackground));

    SplitButton.Foreground = new SolidColorBrush(
        Color.Parse(_theme.Colors.ToolbarForeground));

    SplitButton.BorderBrush = new SolidColorBrush(
        Color.Parse(_theme.Colors.PanelBorder));

    TerminalHost.Background = new SolidColorBrush(
        Color.Parse(_theme.Colors.PanelBackground));

    NavbarBorder.Height = _config.Navbar.Height;
    NavbarBorder.Padding = new Thickness(
        _config.Navbar.Padding);

    NavbarBorder.IsVisible =
        _config.Window.ShowNavbar &&
        _config.Navbar.Visible;

    BrandText.IsVisible = _config.Navbar.ShowBrand;

    Opacity = Math.Clamp(
        _config.Appearance.WindowOpacity, 0.2, 1.0);
    
    ThemeSelector.ItemsSource = ThemeService.GetAvailableThemeNames();
    ThemeSelector.SelectedItem = _config.Appearance.Theme;
    
    foreach (var pane in _panes)
{
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





















    // Ustvari nov terminalski panel.
    private TerminalPane AddPane()
    {
        var terminal = new TerminalControl
        {
            FontSize = _config.Appearance.FontSize,
            ScrollbackLimit = 10000,
            ColorScheme = CreateTerminalTheme()
        };

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
                    App = shell,
                    Cwd = home,
                    Cols = size.Cols,
                    Rows = size.Rows,
                    CommandLine = Array.Empty<string>(),

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

        int index = Math.Clamp(
            _activePaneIndex, 0, _panes.Count - 1);

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
        if (_panes.Count <= 1)
            return;

        _activePaneIndex =
            (_activePaneIndex + 1) % _panes.Count;

        _panes[_activePaneIndex].Terminal.Focus();
    }

    // Preklopi na prejšnji panel.
    private void PreviousPane()
    {
        if (_panes.Count <= 1)
            return;

        _activePaneIndex =
            (_activePaneIndex - 1 + _panes.Count)
            % _panes.Count;

        _panes[_activePaneIndex].Terminal.Focus();
    }

    // Obstoječi gumb za razdelitev še naprej deluje.
    private void OnSplitClicked(
        object? sender,
        RoutedEventArgs e)
    {
        SplitTerminal();
    }

    // Bližnjice na tipkovnici.
    private async void OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.F4)
        {
            Close();
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
            }
        }
    }

    // Ob zaprtju aplikacije zapri vse preostale PTY-povezave.
    private async void OnWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_closing)
            return;

        _closing = true;

        foreach (var pane in _panes.ToArray())
        {
            await pane.Adapter.DisposeAsync();
        }

        _panes.Clear();
    }
}