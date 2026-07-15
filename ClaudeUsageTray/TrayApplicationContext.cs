// version 7
using System.Diagnostics;

namespace ClaudeUsageTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int MinimumManualRefreshAnimationMilliseconds = 2000;
    private const int SpinnerFrameCount = 12;
    private const int HoverAnchorRadius = 20;

    private readonly HomeAssistantClient _homeAssistantClient = new();
    private readonly NotifyIcon _usageNotifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _spinnerTimer;
    private readonly System.Windows.Forms.Timer _hoverMonitorTimer;
    private readonly HoverDetailsForm _hoverDetailsForm;

    private AppSettings _settings;
    private Icon? _usageIcon;
    private UsageSnapshot? _latestSnapshot;
    private bool _refreshInProgress;
    private bool _isExiting;
    private bool _showingClaudeLogo;
    private bool _showingHoverValue;
    private bool _contextMenuOpen;
    private int _spinnerFrame;
    private string? _lastError;
    private Point _trayHoverAnchor;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();
        _hoverDetailsForm = new HoverDetailsForm();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += (_, _) =>
        {
            _contextMenuOpen = true;
            RestoreClaudeLogoIcon();
        };

        _contextMenu.Closed += (_, _) => _contextMenuOpen = false;

        _contextMenu.Items.Add(
            "Refresh Now",
            null,
            async (_, _) => await RefreshUsageAsync(
                showErrorBalloon: true,
                showRefreshAnimation: true));

        _contextMenu.Items.Add(
            "Settings",
            null,
            (_, _) => ShowSettings());

        _contextMenu.Items.Add(new ToolStripSeparator());

        _contextMenu.Items.Add(
            "Open Home Assistant",
            null,
            (_, _) => OpenUrl(_settings.HomeAssistantUrl));

        _contextMenu.Items.Add(
            "Open Claude",
            null,
            (_, _) => OpenUrl("https://claude.ai/"));

        _contextMenu.Items.Add(new ToolStripSeparator());

        _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = true
        };

        _startWithWindowsMenuItem.CheckedChanged += StartWithWindowsChanged;
        _contextMenu.Items.Add(_startWithWindowsMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _usageIcon = IconFactory.CreateClaudeLogoIcon();
        _showingClaudeLogo = true;

        _usageNotifyIcon = new NotifyIcon
        {
            Icon = _usageIcon,
            Text = string.Empty,
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _usageNotifyIcon.MouseClick += UsageNotifyIconMouseClick;
        _usageNotifyIcon.MouseDown += UsageNotifyIconMouseDown;
        _usageNotifyIcon.MouseMove += UsageNotifyIconMouseMove;

        _spinnerTimer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };

        _spinnerTimer.Tick += SpinnerTimerTick;

        _hoverMonitorTimer = new System.Windows.Forms.Timer
        {
            Interval = 75
        };

        _hoverMonitorTimer.Tick += HoverMonitorTimerTick;

        _refreshTimer = new System.Windows.Forms.Timer();
        _refreshTimer.Tick += async (_, _) =>
            await RefreshUsageAsync(
                showErrorBalloon: false,
                showRefreshAnimation: false);

        ApplyRefreshInterval();
        _refreshTimer.Start();

        if (SettingsAreComplete())
        {
            _ = RefreshUsageAsync(
                showErrorBalloon: false,
                showRefreshAnimation: false);
        }
        else
        {
            Application.Idle += ShowInitialSettingsOnIdle;
        }
    }

    private bool SettingsAreComplete()
    {
        return !string.IsNullOrWhiteSpace(_settings.HomeAssistantUrl)
            && !string.IsNullOrWhiteSpace(_settings.AccessToken)
            && !string.IsNullOrWhiteSpace(_settings.UsageEntityId);
    }

    private async void UsageNotifyIconMouseClick(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        RestoreClaudeLogoIcon();

        if (!SettingsAreComplete())
        {
            ShowSettings();
            return;
        }

        await RefreshUsageAsync(
            showErrorBalloon: true,
            showRefreshAnimation: true);
    }

    private void UsageNotifyIconMouseDown(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            RestoreClaudeLogoIcon();
        }
    }

    private void UsageNotifyIconMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (_isExiting || _refreshInProgress || _contextMenuOpen)
        {
            return;
        }

        _trayHoverAnchor = Cursor.Position;
        ShowHoverPresentation();
        _hoverMonitorTimer.Start();
    }

    private async Task RefreshUsageAsync(
        bool showErrorBalloon,
        bool showRefreshAnimation)
    {
        if (_refreshInProgress || _isExiting || !SettingsAreComplete())
        {
            return;
        }

        _refreshInProgress = true;
        Stopwatch animationStopwatch = Stopwatch.StartNew();
        UsageSnapshot? snapshot = null;
        Exception? refreshError = null;

        if (showRefreshAnimation)
        {
            StartRefreshAnimation();
        }

        try
        {
            snapshot = await _homeAssistantClient.GetUsageAsync(_settings);
        }
        catch (Exception ex)
        {
            refreshError = ex;
        }

        try
        {
            if (showRefreshAnimation)
            {
                int remainingMilliseconds =
                    MinimumManualRefreshAnimationMilliseconds
                    - (int)animationStopwatch.ElapsedMilliseconds;

                if (remainingMilliseconds > 0)
                {
                    await Task.Delay(remainingMilliseconds);
                }

                StopRefreshAnimation();
            }

            if (_isExiting)
            {
                return;
            }

            if (refreshError is null && snapshot is not null)
            {
                UpdateUsageDisplay(snapshot);
                _lastError = null;
                return;
            }

            string errorMessage =
                refreshError?.Message
                ?? "Unable to retrieve Claude usage.";

            if (_latestSnapshot is null)
            {
                UpdateUnavailableDisplay();
            }
            else if (_showingHoverValue)
            {
                ShowHoverPresentation(forceIconRefresh: true);
            }
            else
            {
                RestoreClaudeLogoIcon();
            }

            if (showErrorBalloon
                || !string.Equals(
                    _lastError,
                    errorMessage,
                    StringComparison.Ordinal))
            {
                _usageNotifyIcon.BalloonTipTitle = "Claude Usage Tray";
                _usageNotifyIcon.BalloonTipText = errorMessage;
                _usageNotifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _usageNotifyIcon.ShowBalloonTip(5000);
            }

            _lastError = errorMessage;
        }
        finally
        {
            if (showRefreshAnimation)
            {
                StopRefreshAnimation();
            }

            _refreshInProgress = false;
        }
    }

    private void StartRefreshAnimation()
    {
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _showingHoverValue = false;
        _spinnerFrame = 0;

        ReplaceUsageIcon(
            IconFactory.CreateSpinnerIcon(_spinnerFrame));

        _showingClaudeLogo = false;
        _usageNotifyIcon.Text = "Refreshing Claude usage…";
        _spinnerTimer.Start();
    }

    private void StopRefreshAnimation()
    {
        _spinnerTimer.Stop();
    }

    private void SpinnerTimerTick(object? sender, EventArgs e)
    {
        if (!_refreshInProgress || _isExiting)
        {
            _spinnerTimer.Stop();
            return;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrameCount;

        ReplaceUsageIcon(
            IconFactory.CreateSpinnerIcon(_spinnerFrame));

        _showingClaudeLogo = false;
        _showingHoverValue = false;
    }

    private void HoverMonitorTimerTick(object? sender, EventArgs e)
    {
        if (!_showingHoverValue || _refreshInProgress || _contextMenuOpen)
        {
            _hoverMonitorTimer.Stop();
            _hoverDetailsForm.HideDetails();
            return;
        }

        Point cursor = Cursor.Position;

        var hoverZone = new Rectangle(
            _trayHoverAnchor.X - HoverAnchorRadius,
            _trayHoverAnchor.Y - HoverAnchorRadius,
            HoverAnchorRadius * 2,
            HoverAnchorRadius * 2);

        if (hoverZone.Contains(cursor))
        {
            return;
        }

        RestoreClaudeLogoIcon();
    }

    private void UpdateUsageDisplay(UsageSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        _hoverDetailsForm.SetSnapshot(snapshot);

        if (_showingHoverValue)
        {
            ShowHoverPresentation(forceIconRefresh: true);
        }
        else
        {
            RestoreClaudeLogoIcon();
        }

        _usageNotifyIcon.Text = string.Empty;
    }

    private void UpdateUnavailableDisplay()
    {
        _latestSnapshot = null;
        _hoverDetailsForm.SetSnapshot(null);

        if (_showingHoverValue)
        {
            ShowHoverPresentation(forceIconRefresh: true);
        }
        else
        {
            RestoreClaudeLogoIcon();
        }

        _usageNotifyIcon.Text = string.Empty;
    }

    private void ShowHoverPresentation(bool forceIconRefresh = false)
    {
        if (_isExiting || _refreshInProgress || _contextMenuOpen)
        {
            return;
        }

        if (forceIconRefresh || !_showingHoverValue)
        {
            ShowHoverValueIcon();
        }

        _hoverDetailsForm.SetSnapshot(_latestSnapshot);
        _hoverDetailsForm.ShowNear(_trayHoverAnchor);
    }

    private void ShowHoverValueIcon()
    {
        Icon percentageIcon = _latestSnapshot is null
            ? IconFactory.CreateUnavailablePercentageIcon(
                _settings.HoverValueFontSize)
            : IconFactory.CreatePercentageIcon(
                _latestSnapshot.UsagePercentage,
                _settings.HoverValueFontSize);

        ReplaceUsageIcon(percentageIcon);
        _showingClaudeLogo = false;
        _showingHoverValue = true;
        _usageNotifyIcon.Text = string.Empty;
    }

    private void RestoreClaudeLogoIcon()
    {
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _showingHoverValue = false;

        if (_showingClaudeLogo)
        {
            return;
        }

        ReplaceUsageIcon(IconFactory.CreateClaudeLogoIcon());
        _showingClaudeLogo = true;
        _usageNotifyIcon.Text = string.Empty;
    }

    private void ReplaceUsageIcon(Icon replacement)
    {
        Icon? previous = _usageIcon;
        _usageIcon = replacement;
        _usageNotifyIcon.Icon = replacement;
        previous?.Dispose();
    }

    private void ShowSettings()
    {
        RestoreClaudeLogoIcon();

        using var form = new SettingsForm(
            _settings,
            _homeAssistantClient);

        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = form.EditedSettings;

        try
        {
            SettingsStore.Save(_settings);
            StartupManager.SetEnabled(_settings.StartWithWindows);

            _startWithWindowsMenuItem.CheckedChanged -=
                StartWithWindowsChanged;

            _startWithWindowsMenuItem.Checked =
                _settings.StartWithWindows;

            _startWithWindowsMenuItem.CheckedChanged +=
                StartWithWindowsChanged;

            ApplyRefreshInterval();

            _ = RefreshUsageAsync(
                showErrorBalloon: true,
                showRefreshAnimation: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Unable to Save Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartWithWindowsChanged(object? sender, EventArgs e)
    {
        try
        {
            _settings.StartWithWindows =
                _startWithWindowsMenuItem.Checked;

            StartupManager.SetEnabled(_settings.StartWithWindows);
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            _startWithWindowsMenuItem.CheckedChanged -=
                StartWithWindowsChanged;

            _startWithWindowsMenuItem.Checked =
                !_startWithWindowsMenuItem.Checked;

            _startWithWindowsMenuItem.CheckedChanged +=
                StartWithWindowsChanged;

            MessageBox.Show(
                ex.Message,
                "Startup Setting Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyRefreshInterval()
    {
        _refreshTimer.Interval =
            Math.Clamp(_settings.RefreshSeconds, 30, 3600) * 1000;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Unable to Open Link",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowInitialSettingsOnIdle(object? sender, EventArgs e)
    {
        Application.Idle -= ShowInitialSettingsOnIdle;
        ShowSettings();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _refreshTimer.Stop();
        _spinnerTimer.Stop();
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _usageNotifyIcon.Visible = false;

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _spinnerTimer.Dispose();
            _hoverMonitorTimer.Dispose();
            _hoverDetailsForm.Dispose();
            _usageNotifyIcon.Dispose();
            _contextMenu.Dispose();
            _homeAssistantClient.Dispose();
            _usageIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
