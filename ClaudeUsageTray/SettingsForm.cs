// version 7
namespace ClaudeUsageTray;

internal sealed class SettingsForm : Form
{
    private static readonly Color PreviewBackground = Color.FromArgb(35, 37, 42);

    private readonly TextBox _urlTextBox = new();
    private readonly TextBox _tokenTextBox = new();
    private readonly TextBox _usageEntityTextBox = new();
    private readonly TextBox _resetEntityTextBox = new();
    private readonly NumericUpDown _refreshSecondsInput = new();
    private readonly NumericUpDown _hoverValueFontSizeInput = new();
    private readonly PictureBox _fontSizePreviewPictureBox = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly Button _testButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label _statusLabel = new();
    private readonly HomeAssistantClient _client;

    public AppSettings EditedSettings { get; }

    public SettingsForm(AppSettings currentSettings, HomeAssistantClient client)
    {
        _client = client;
        EditedSettings = currentSettings.Clone();

        Text = "Claude Usage Tray Settings";
        Icon = IconFactory.CreateClaudeLogoIcon();
        ClientSize = new Size(640, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildInterface();
        LoadValues();
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 11
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Home Assistant URL", _urlTextBox, 38);

        _tokenTextBox.UseSystemPasswordChar = true;
        AddRow(layout, 1, "Long-lived token", _tokenTextBox, 38);

        AddRow(layout, 2, "Usage entity", _usageEntityTextBox, 38);
        AddRow(layout, 3, "Reset-time entity", _resetEntityTextBox, 38);

        _refreshSecondsInput.Minimum = 30;
        _refreshSecondsInput.Maximum = 3600;
        _refreshSecondsInput.Increment = 30;
        _refreshSecondsInput.Width = 110;
        AddRow(layout, 4, "Refresh interval", _refreshSecondsInput, 38);

        _hoverValueFontSizeInput.Minimum = 24;
        _hoverValueFontSizeInput.Maximum = 62;
        _hoverValueFontSizeInput.Increment = 1;
        _hoverValueFontSizeInput.Width = 110;
        _hoverValueFontSizeInput.ValueChanged += (_, _) => UpdateFontSizePreview();
        AddRow(layout, 5, "Hover percentage font size", _hoverValueFontSizeInput, 38);

        _fontSizePreviewPictureBox.BackColor = PreviewBackground;
        _fontSizePreviewPictureBox.Size = new Size(88, 78);
        _fontSizePreviewPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
        _fontSizePreviewPictureBox.Margin = new Padding(3, 4, 3, 4);

        layout.Controls.Add(new Label
        {
            Text = "Tray icon preview",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        }, 0, 6);
        layout.Controls.Add(_fontSizePreviewPictureBox, 1, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));

        _startWithWindowsCheckBox.Text = "Start Claude Usage Tray when I sign in to Windows";
        _startWithWindowsCheckBox.AutoSize = true;
        layout.Controls.Add(new Label(), 0, 7);
        layout.Controls.Add(_startWithWindowsCheckBox, 1, 7);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var helpLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Hover over the Claude tray icon to replace it with the current percentage and show a compact usage window containing the value and live reset countdown. Move the pointer away to restore the Claude logo."
        };
        layout.Controls.Add(new Label(), 0, 8);
        layout.Controls.Add(helpLabel, 1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        _statusLabel.AutoSize = true;
        _statusLabel.MaximumSize = new Size(390, 0);
        _statusLabel.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(new Label(), 0, 9);
        layout.Controls.Add(_statusLabel, 1, 9);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _testButton.Text = "Test Connection";
        _testButton.AutoSize = true;
        _testButton.Click += async (_, _) => await TestConnectionAsync();

        _saveButton.Text = "Save";
        _saveButton.AutoSize = true;
        _saveButton.Click += (_, _) => SaveAndClose();

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_testButton);

        layout.Controls.Add(new Label(), 0, 10);
        layout.Controls.Add(buttons, 1, 10);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        Controls.Add(layout);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private static void AddRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Control inputControl,
        int rowHeight)
    {
        var label = new Label
        {
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        inputControl.Dock = inputControl is NumericUpDown ? DockStyle.Left : DockStyle.Fill;
        inputControl.Margin = new Padding(3, 6, 3, 6);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(inputControl, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
    }

    private void LoadValues()
    {
        _urlTextBox.Text = EditedSettings.HomeAssistantUrl;
        _tokenTextBox.Text = EditedSettings.AccessToken;
        _usageEntityTextBox.Text = EditedSettings.UsageEntityId;
        _resetEntityTextBox.Text = EditedSettings.ResetEntityId;
        _refreshSecondsInput.Value = Math.Clamp(EditedSettings.RefreshSeconds, 30, 3600);
        _hoverValueFontSizeInput.Value = Math.Clamp(EditedSettings.HoverValueFontSize, 24, 62);
        _startWithWindowsCheckBox.Checked = EditedSettings.StartWithWindows;
        UpdateFontSizePreview();
    }

    private void UpdateFontSizePreview()
    {
        using Icon previewIcon = IconFactory.CreatePercentageIcon(
            76m,
            (int)_hoverValueFontSizeInput.Value);

        Bitmap replacement = previewIcon.ToBitmap();
        Image? previous = _fontSizePreviewPictureBox.Image;
        _fontSizePreviewPictureBox.Image = replacement;
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontSizePreviewPictureBox.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private AppSettings ReadValues()
    {
        return new AppSettings
        {
            SettingsSchemaVersion = 7,
            HomeAssistantUrl = _urlTextBox.Text,
            AccessToken = _tokenTextBox.Text,
            EncryptedAccessToken = EditedSettings.EncryptedAccessToken,
            UsageEntityId = _usageEntityTextBox.Text,
            ResetEntityId = _resetEntityTextBox.Text,
            RefreshSeconds = (int)_refreshSecondsInput.Value,
            HoverValueFontSize = (int)_hoverValueFontSizeInput.Value,
            StartWithWindows = _startWithWindowsCheckBox.Checked
        };
    }

    private async Task TestConnectionAsync()
    {
        SetBusy(true, "Testing Home Assistant connection...");

        try
        {
            AppSettings candidate = ReadValues();
            candidate.Normalise();
            UsageSnapshot snapshot = await _client.GetUsageAsync(candidate);
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text =
                $"Connected successfully. Current Claude usage: {snapshot.UsagePercentage:0.#}%";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _testButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        UseWaitCursor = busy;
        _statusLabel.Text = status;
    }

    private void SaveAndClose()
    {
        AppSettings candidate = ReadValues();
        candidate.Normalise();

        if (!Uri.TryCreate(candidate.HomeAssistantUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(
                this,
                "Enter a valid Home Assistant URL beginning with http:// or https://.",
                "Invalid URL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.AccessToken))
        {
            MessageBox.Show(
                this,
                "Enter a Home Assistant long-lived access token.",
                "Token Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.UsageEntityId))
        {
            MessageBox.Show(
                this,
                "Enter the Claude usage entity ID.",
                "Entity Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        EditedSettings.SettingsSchemaVersion = 7;
        EditedSettings.HomeAssistantUrl = candidate.HomeAssistantUrl;
        EditedSettings.AccessToken = candidate.AccessToken;
        EditedSettings.EncryptedAccessToken = candidate.EncryptedAccessToken;
        EditedSettings.UsageEntityId = candidate.UsageEntityId;
        EditedSettings.ResetEntityId = candidate.ResetEntityId;
        EditedSettings.RefreshSeconds = candidate.RefreshSeconds;
        EditedSettings.HoverValueFontSize = candidate.HoverValueFontSize;
        EditedSettings.StartWithWindows = candidate.StartWithWindows;

        DialogResult = DialogResult.OK;
        Close();
    }
}
