using System.Globalization;
using System.Reflection;
using System.Drawing.Printing;
using Oeps.RawMaterialSticker.App.Printing;
using Oeps.RawMaterialSticker.Core;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Printing;
using Oeps.RawMaterialSticker.Core.Updates;

namespace Oeps.RawMaterialSticker.App;

public sealed class MainForm : Form
{
    private bool PrinterTest => _args.Contains("--printer-test") && !_config.SampleMode;
    private readonly AppConfiguration _config;
    private readonly AppPaths _paths;
    private readonly UserSettings _settings;
    private readonly ComponentRepository _repository;
    private readonly HttpClient _http;
    private readonly string[] _args;
    private readonly OperationSession _session = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ComboBox _printer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Printer" };
    private readonly Button _printerSettings = new() { Text = "⚙", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, AccessibleName = "Printer offsets", Margin = new Padding(6, 0, 0, 0), TabStop = true };
    private readonly RadioButton _oepsMode = SearchModeButton("&OEPS PN");
    private readonly RadioButton _mpnMode = SearchModeButton("&MPN");
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search part numbers…", AccessibleName = "Component search", BorderStyle = BorderStyle.None };
    private readonly ListBox _suggestions = new() { Visible = false, IntegralHeight = false, AccessibleName = "Component suggestions", DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _pn = ReadOnlyBox("Selected OEPS PN");
    private readonly TextBox _mpn = ReadOnlyBox("Selected MPN");
    private readonly TextBox _description = ReadOnlyBox("Selected description");
    private readonly CheckBox _extendedDescription = new() { Text = "&Extended description", AutoSize = true, Margin = new Padding(0, 0, 0, 0) };
    private Panel _descriptionFrame = null!;
    private readonly ComboBox _month = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, AccessibleName = "Reception month" };
    private readonly ComboBox _year = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, AccessibleName = "Reception year" };
    private readonly ComboBox _packaging = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, AccessibleName = "Lot packaging" };
    private readonly NumericUpDown _quantity = new() { Width = 77, Minimum = 0, Maximum = int.MaxValue, DecimalPlaces = 0, AccessibleName = "Component quantity" };
    private readonly CheckBox _decimalQuantity = new() { Text = "Use decimal number", AutoSize = true, Margin = Padding.Empty, AccessibleName = "Use decimal quantity" };
    private readonly CheckBox _dryRun = new() { Text = "&Dry run", AutoSize = true };
    private readonly CheckBox _monthUnavailable = new() { Text = "Use '00'", AutoSize = true, CheckAlign = ContentAlignment.MiddleLeft, AccessibleName = "Use 00 for month", Margin = Padding.Empty };
    private readonly CheckBox _yearUnavailable = new() { Text = "Use '00'", AutoSize = true, CheckAlign = ContentAlignment.MiddleLeft, AccessibleName = "Use 00 for year", Margin = Padding.Empty };
    private readonly Button _print = new() { Text = "Print sticker", Dock = DockStyle.Fill, BackColor = Color.FromArgb(0, 103, 192), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button _refresh = new() { Text = "&Update database", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(0, 93, 174) };
    private readonly Label _dateHint = new() { AutoSize = true, ForeColor = Color.FromArgb(87, 99, 114), Margin = new Padding(9, 6, 0, 0) };
    private readonly Label _validation = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(135, 65, 15), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _syncStatus = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(87, 99, 114), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _syncIndicator = new() { Text = "●", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _jobStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000 };
    private Control _searchFrame = null!;
    private readonly LinkLabel _updateStatus = new() { AutoSize = true, LinkColor = Color.FromArgb(28, 95, 171), Text = "" };
    private readonly string _version = (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0").Split('+')[0];
    private LabelRenderer? _renderer;
    private LabelJobRenderer? _jobRenderer;
    private string? _expensiveTemplateError;
    private string? _templateError;
    private string? _printerError;
    private bool _submitting, _changingSearch, _checkingRelease;
    private DateTimeOffset _nextReleaseCheck = DateTimeOffset.MinValue;

    public MainForm(AppConfiguration config, AppPaths paths, HttpClient http, string[] args)
    {
        SuspendLayout();
        _config = config; _paths = paths; _http = http; _args = args;
        _settings = UserSettings.Load(paths.SettingsFile);
        _repository = new ComponentRepository(http, config, paths);
        Text = "OEPS Raw Material Sticker";
        if (PrinterTest) Text += " — PRINTER TEST (L3 + L3 expensive)";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("Oeps.AppIcon"))
        {
            if (iconStream is not null)
            {
                using var appIcon = new Icon(iconStream);
                Icon = (Icon)appIcon.Clone();
            }
        }
        if (args.Contains("--ui-smoke")) { ShowInTaskbar = false; Opacity = 0; }
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(248, 250, 252);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(_settings.WindowWidth, _settings.WindowHeight);
        MinimumSize = SizeFromClientSize(new Size(560, UserSettings.MinimumWindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        if (_settings.WindowX is int x && _settings.WindowY is int y && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(x, y, 100, 100))))
        { StartPosition = FormStartPosition.Manual; Location = new Point(x, y); }
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        BuildLayout();
        _month.Items.AddRange(Enumerable.Range(1, 12).Select(n => (object)n.ToString("00")).ToArray());
        _year.Items.AddRange(Enumerable.Range(2020, DateTime.Now.Year - 2020 + 1).Select(n => (object)n.ToString("0000")).ToArray());
        _month.SelectedItem = _session.Month; _year.Text = _session.Year;
        _quantity.Text = "";
        _packaging.Items.AddRange(LabelValues.PackagingCodes.Keys.Cast<object>().ToArray());
        _packaging.SelectedItem = _session.Packaging;
        _dryRun.Checked = config.DryRun || config.SampleMode;
        _dryRun.Enabled = !config.SampleMode;
        _oepsMode.Checked = _settings.SearchMode == SearchMode.OepsPn; _mpnMode.Checked = !_oepsMode.Checked;
        _extendedDescription.Checked = _settings.ExtendedDescription;
        StyleSearchModes();
        LoadPrinters();
        try
        {
            var templatePath = Path.IsPathRooted(config.Printer.TemplatePath) ? config.Printer.TemplatePath : Path.Combine(AppContext.BaseDirectory, config.Printer.TemplatePath);
            _renderer = new LabelRenderer(File.ReadAllText(templatePath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { _templateError = "Template unavailable: " + e.Message; }
        ExpensiveLabelRenderer? expensiveRenderer = null;
        try
        {
            var extraTemplate = config.ExpensivePrinter?.TemplatePath ?? config.Printer.ExpensiveTemplatePath;
            var path = Path.IsPathRooted(extraTemplate) ? extraTemplate : Path.Combine(AppContext.BaseDirectory, extraTemplate);
            expensiveRenderer = new ExpensiveLabelRenderer(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { _expensiveTemplateError = "L3 expensive template unavailable: " + e.Message; }
        if (_renderer is not null) _jobRenderer = new LabelJobRenderer(_renderer, expensiveRenderer);
        _repository.LoadCache();
        _search.TextChanged += (_, _) => { if (!_changingSearch) { _session.InvalidateSelection(); ShowSuggestions(); UpdateValidation(); } };
        _search.KeyDown += SearchKeyDown;
        _search.Enter += (_, _) => { if (_session.Selected is null) ShowSuggestions(); };
        _search.Leave += (_, _) => HideSuggestionsAfterFocusChange();
        _suggestions.Leave += (_, _) => HideSuggestionsAfterFocusChange();
        _suggestions.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SelectSuggestion(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { _search.Focus(); HideSuggestions(); e.SuppressKeyPress = true; } };
        _suggestions.DrawItem += DrawSuggestion;
        _suggestions.MouseClick += (_, e) => { if (_suggestions.IndexFromPoint(e.Location) >= 0) SelectSuggestion(); };
        _oepsMode.CheckedChanged += (_, _) => { StyleSearchModes(); _session.InvalidateSelection(); ShowSuggestions(); UpdateValidation(); };
        _mpnMode.CheckedChanged += (_, _) => StyleSearchModes();
        _printer.SelectedIndexChanged += (_, _) => { _printerError = null; UpdateValidation(); };
        _printer.DropDown += (_, _) => LoadPrinters();
        _printerSettings.Click += (_, _) => ConfigurePrinterOffsets();
        _toolTip.SetToolTip(_printerSettings, "Configure offsets for the selected printer");
        _month.SelectedIndexChanged += (_, _) => { _session.Month = _month.Text; UpdateValidation(); };
        _year.TextChanged += (_, _) => { _session.Year = _year.Text; UpdateValidation(); };
        _packaging.SelectedIndexChanged += (_, _) => { _session.Packaging = _packaging.Text; UpdateValidation(); };
        _monthUnavailable.CheckedChanged += (_, _) =>
        {
            _session.MonthUnavailable = _monthUnavailable.Checked;
            _month.Enabled = !_monthUnavailable.Checked;
            UpdateValidation();
        };
        _yearUnavailable.CheckedChanged += (_, _) =>
        {
            _session.YearUnavailable = _yearUnavailable.Checked;
            _year.Enabled = !_yearUnavailable.Checked;
            UpdateValidation();
        };
        _quantity.TextChanged += (_, _) => { _session.Quantity = _quantity.Text; UpdateValidation(); };
        _quantity.ValueChanged += (_, _) => { _session.Quantity = _quantity.Text; UpdateValidation(); };
        _decimalQuantity.CheckedChanged += (_, _) =>
        {
            if (!_decimalQuantity.Checked) _quantity.Value = decimal.Truncate(_quantity.Value);
            _quantity.DecimalPlaces = _decimalQuantity.Checked ? 3 : 0;
            UpdateValidation();
        };
        _dryRun.CheckedChanged += (_, _) => UpdateValidation();
        _extendedDescription.CheckedChanged += (_, _) => UpdateValidation();
        _print.Click += async (_, _) => await SubmitAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _updateStatus.LinkClicked += (_, _) => { _jobStatus.Text = "Restart this app when ready to install the update. You can choose ‘Not now’ to skip the update."; };
        _timer.Tick += async (_, _) =>
        {
            UpdateFooter();
            if (!_config.SampleMode && !_repository.IsRefreshing && DateTimeOffset.UtcNow >= _repository.NextRefreshUtc) await RefreshAsync();
            if (!_checkingRelease && DateTimeOffset.UtcNow >= _nextReleaseCheck) await CheckReleaseAsync();
        };
        Shown += async (_, _) =>
        {
            AppInstanceCoordinator.SignalReadyFromArguments(args);
            _timer.Start(); _search.Focus(); UpdateValidation();
            if (args.Contains("--ui-smoke")) { await RunUiSmokeAsync(); return; }
            await Task.WhenAll(RefreshAsync(), CheckReleaseAsync());
        };
        FormClosing += OnClosing;
        Deactivate += (_, _) => HideSuggestions();
        Resize += (_, _) => HideSuggestions();
        UpdateValidation(); UpdateFooter();
        if (_settings.LoadError is not null) _jobStatus.Text = _settings.LoadError;
        ResumeLayout(true);
    }

    private static TextBox ReadOnlyBox(string name) => new() { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(239, 243, 247), AccessibleName = name, TabStop = false };
    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0) };
    private static RadioButton SearchModeButton(string text) => new() { Text = text, Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty };
    private void StyleSearchModes()
    {
        foreach (var button in new[] { _oepsMode, _mpnMode })
        {
            button.BackColor = button.Checked ? Color.FromArgb(0, 103, 192) : Color.FromArgb(239, 243, 247);
            button.ForeColor = button.Checked ? Color.White : Color.FromArgb(32, 44, 58);
            button.FlatAppearance.BorderColor = button.Checked ? Color.FromArgb(0, 103, 192) : Color.FromArgb(190, 200, 212);
            button.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 103, 192);
        }
    }
    private static Panel InputFrame(Control input, Color background, bool searchIcon = false)
    {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = background, Padding = new Padding(9, 6, 9, 5), Margin = new Padding(0, 4, 0, 4) };
        frame.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, frame.ClientRectangle, Color.FromArgb(199, 207, 217), ButtonBorderStyle.Solid);
        if (searchIcon)
        {
            var icon = new Panel { Dock = DockStyle.Right, Width = 23, Cursor = Cursors.IBeam };
            icon.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(95, 107, 122), 1.7f);
                e.Graphics.DrawEllipse(pen, 3, 1, 11, 11); e.Graphics.DrawLine(pen, 13, 11, 19, 17);
            };
            icon.Click += (_, _) => input.Focus();
            frame.Controls.Add(input); frame.Controls.Add(icon);
        }
        else frame.Controls.Add(input);
        return frame;
    }
    private void BuildLayout()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 0) };
        scroll.Scroll += (_, _) => HideSuggestions();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control control, int height) { layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); layout.Controls.Add(control, 0, layout.RowCount - 1); }
        var printerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(12, 0, 0, 0) };
        printerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        printerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); printerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        printerRow.Controls.Add(Caption("&Printer"), 0, 0);
        var printerControls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        printerControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        printerControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        printerControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        _printer.Anchor = AnchorStyles.Left | AnchorStyles.Right; _printer.Dock = DockStyle.None;
        _printerSettings.Font = new Font("Segoe UI Symbol", 10f);
        _printerSettings.Dock = DockStyle.None;
        _printerSettings.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _printerSettings.Height = _printer.Height;
        _printer.SizeChanged += (_, _) => _printerSettings.Height = _printer.Height;
        _printerSettings.FlatAppearance.BorderColor = Color.FromArgb(199, 207, 217);
        printerControls.Controls.Add(_printer, 0, 0); printerControls.Controls.Add(_printerSettings, 1, 0);
        printerRow.Controls.Add(printerControls, 1, 0);
        Row(printerRow, 38); Row(new Panel(), 8);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, BackColor = Color.White, Padding = new Padding(12), Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Paint += (_, e) =>
        {
            var borderColor = Color.FromArgb(219, 226, 234);
            ControlPaint.DrawBorder(e.Graphics, fields.ClientRectangle, borderColor, ButtonBorderStyle.Solid);
            var separatorY = fields.Padding.Top + fields.GetRowHeights().Take(4).Sum();
            using var separatorPen = new Pen(borderColor);
            e.Graphics.DrawLine(separatorPen, fields.Padding.Left, separatorY, fields.ClientSize.Width - fields.Padding.Right, separatorY);
        };
        for (var i = 0; i < 7; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 2 ? 68 : i == 3 ? 64 : i == 4 ? 50 : 40));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        void Field(string caption, Control control, int row)
        {
            var label = Caption(caption); label.TabIndex = row * 2;
            if (row == 4)
            {
                label.Margin = new Padding(0, 10, 8, 0);
                control.Margin = new Padding(0, 14, 0, 4);
            }
            control.TabIndex = row * 2 + 1;
            fields.Controls.Add(label, 0, row); fields.Controls.Add(control, 1, row);
        }
        var modes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 3, 0, 4) };
        modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        modes.Controls.Add(_oepsMode, 0, 0); modes.Controls.Add(_mpnMode, 1, 0); Field("Search by", modes, 0);
        _searchFrame = InputFrame(_search, Color.White, searchIcon: true); Field("&Component", _searchFrame, 1);
        Field("OEPS PN", InputFrame(_pn, _pn.BackColor), 4); Field("MPN", InputFrame(_mpn, _mpn.BackColor), 5);
        _description.TabStop = true;
        _descriptionFrame = InputFrame(_description, _description.BackColor);
        Field("Description", _descriptionFrame, 6);
        _extendedDescription.TabIndex = 15;
        fields.Controls.Add(_extendedDescription, 1, 7);
        var date = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 4, Margin = new Padding(0, 6, 0, 0) };
        date.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); date.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        date.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        date.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        date.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        date.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _month.Margin = new Padding(0, 0, 8, 0); _year.Margin = new Padding(0, 0, 8, 0); _packaging.Margin = Padding.Empty;
        _dateHint.Font = new Font(Font.FontFamily, 9f); _dateHint.Margin = new Padding(6, 5, 0, 0);
        _dateHint.AutoSize = false; _dateHint.Dock = DockStyle.Fill;
        _monthUnavailable.Font = _yearUnavailable.Font = _dateHint.Font;
        date.Controls.Add(_month, 0, 0); date.Controls.Add(_year, 1, 0);
        date.Controls.Add(_packaging, 2, 0); date.Controls.Add(_dateHint, 3, 0);
        date.Controls.Add(_monthUnavailable, 0, 1); date.Controls.Add(_yearUnavailable, 1, 1); Field("&Lot (batch)", date, 2);
        var quantityRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        _quantity.Margin = new Padding(0, 0, 12, 0);
        quantityRow.Controls.AddRange([_quantity, new Label { Text = "Components in package (0 = unknown)", AutoSize = true, ForeColor = Color.FromArgb(87, 99, 114), Margin = new Padding(0, 5, 0, 0) }]);
        var quantityFields = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        quantityFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        quantityFields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _decimalQuantity.Font = _dateHint.Font;
        quantityFields.Controls.Add(quantityRow, 0, 0); quantityFields.Controls.Add(_decimalQuantity, 0, 1);
        Field("&Quantity", quantityFields, 3);
        Row(fields, 390); Row(new Panel(), 6);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _refresh.Margin = new Padding(0, 0, 8, 0); _print.Margin = Padding.Empty;
        _refresh.FlatAppearance.BorderColor = _refresh.ForeColor; _print.FlatAppearance.BorderSize = 0;
        _print.Font = new Font(Font, FontStyle.Bold);
        buttons.Controls.Add(_refresh, 0, 0); buttons.Controls.Add(_print, 1, 0); Row(buttons, 38);
        var validationRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        validationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); validationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _validation.Font = new Font(Font.FontFamily, 8.5f); _jobStatus.Font = _validation.Font;
        validationRow.Controls.Add(_validation, 0, 0); Row(validationRow, 30); Row(_jobStatus, 29);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 35, ColumnCount = 4, Padding = new Padding(16, 0, 12, 0), BackColor = Color.FromArgb(240, 244, 248), Font = new Font(Font.FontFamily, 8.5f) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 19)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_syncIndicator, 0, 0); footer.Controls.Add(_syncStatus, 1, 0); footer.Controls.Add(Caption($"│  v{_version}"), 2, 0);
        _updateStatus.Anchor = AnchorStyles.Right; footer.Controls.Add(_updateStatus, 3, 0);
        scroll.Controls.Add(layout); Controls.Add(scroll); Controls.Add(footer); Controls.Add(_suggestions);
        _suggestions.ItemHeight = 28;
        void DismissOnClick(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control == _searchFrame || control == _suggestions) continue;
                control.MouseDown += (_, _) => HideSuggestions(); DismissOnClick(control);
            }
        }
        DismissOnClick(this);
        _jobStatus.TextChanged += (_, _) => _toolTip.SetToolTip(_jobStatus, _jobStatus.Text);
    }

    private void LoadPrinters()
    {
        var intended = _printer.SelectedItem as string ?? _settings.LastPrinterName;
        try
        {
            var printers = PrinterSettings.InstalledPrinters.Cast<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
            _printer.BeginUpdate(); _printer.Items.Clear(); _printer.Items.AddRange(printers); _printer.SelectedIndex = -1;
            if (intended is not null && printers.Contains(intended, StringComparer.Ordinal)) _printer.SelectedItem = intended;
            _printer.EndUpdate();
            _printerError = intended is not null && _printer.SelectedIndex < 0 ? $"Saved printer '{intended}' is unavailable. Select a printer explicitly." : printers.Length == 0 ? "No installed printer queues. Dry runs are available." : null;
        }
        catch (Exception e) { _printerError = "Cannot list printer queues: " + e.Message; }
        if (_printerError is not null) _jobStatus.Text = _printerError;
        UpdateValidation();
    }

    private void ConfigurePrinterOffsets()
    {
        if (_submitting || _printer.SelectedItem is not string queue) return;
        using var dialog = new PrinterOffsetsDialog(queue, _settings.GetPrinterOffsets(queue));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var existed = _settings.PrinterOffsets.TryGetValue(queue, out var previous);
        try
        {
            dialog.Offsets.Validate();
            _settings.PrinterOffsets[queue] = dialog.Offsets;
            _settings.Save(_paths.SettingsFile);
            _jobStatus.Text = $"Offsets saved for {queue}: X {dialog.Offsets.XMm:0.##} mm, Y {dialog.Offsets.YMm:0.##} mm.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (existed) _settings.PrinterOffsets[queue] = previous!;
            else _settings.PrinterOffsets.Remove(queue);
            MessageBox.Show(this, "Could not save printer offsets: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateValidation();
    }

    private void ShowSuggestions()
    {
        var found = ComponentSearch.Search(_repository.Components, _search.Text, _oepsMode.Checked ? SearchMode.OepsPn : SearchMode.Mpn);
        _suggestions.BeginUpdate(); _suggestions.Items.Clear();
        foreach (var c in found) _suggestions.Items.Add(new Suggestion(c));
        _suggestions.SelectedIndex = -1; _suggestions.EndUpdate();
        if ((_search.Focused || _suggestions.Focused) && !string.IsNullOrWhiteSpace(_search.Text) && _session.Selected is null && found.Count > 0)
            OpenSuggestions();
        else HideSuggestions();
    }
    private void OpenSuggestions()
    {
        if (_suggestions.Items.Count == 0) return;
        var location = PointToClient(_searchFrame.PointToScreen(new Point(0, _searchFrame.Height)));
        var height = Math.Min(6, _suggestions.Items.Count) * _suggestions.ItemHeight + 2;
        _suggestions.SetBounds(location.X, location.Y, _searchFrame.Width, Math.Min(height, Math.Max(_suggestions.ItemHeight + 2, ClientSize.Height - location.Y - 38)));
        _suggestions.BringToFront(); _suggestions.Show();
    }
    private void HideSuggestions() { _suggestions.Hide(); _suggestions.ClearSelected(); }
    private void HideSuggestionsAfterFocusChange()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() => { if (!IsDisposed && !_search.ContainsFocus && !_suggestions.ContainsFocus) HideSuggestions(); });
    }
    private void DrawSuggestion(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _suggestions.Items[e.Index] is not Suggestion suggestion) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(217, 234, 253) : Color.White);
        e.Graphics.FillRectangle(background, e.Bounds);
        var pnWidth = Math.Min(e.Bounds.Width / 2, (int)(150 * DeviceDpi / 96f));
        var pnBounds = new Rectangle(e.Bounds.X + 9, e.Bounds.Y, pnWidth - 12, e.Bounds.Height);
        var mpnBounds = new Rectangle(e.Bounds.X + pnWidth, e.Bounds.Y, e.Bounds.Width - pnWidth - 9, e.Bounds.Height);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, suggestion.DisplayOepsPn, Font, pnBounds, Color.FromArgb(32, 44, 58), flags);
        TextRenderer.DrawText(e.Graphics, suggestion.Component.Mpn, Font, mpnBounds, Color.FromArgb(70, 84, 102), flags);
    }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up && _suggestions.Items.Count > 0)
        { OpenSuggestions(); _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, _suggestions.Items.Count - 1); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter && _suggestions.Visible) { SelectSuggestion(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape) { HideSuggestions(); e.SuppressKeyPress = true; }
    }
    private void SelectSuggestion()
    {
        if (_suggestions.SelectedItem is not Suggestion item) return;
        _session.Select(item.Component);
        _changingSearch = true;
        _search.Text = _oepsMode.Checked ? item.Component.OepsPn : item.Component.Mpn;
        _changingSearch = false;
        HideSuggestions();
        _jobStatus.Text = ""; UpdateValidation(); _quantity.Focus();
    }
    private LabelRequest? GetRequest(out string? error)
    {
        error = null;
        if (_session.Selected is not { } c) { error = _repository.HasData ? "Select a component pairing from the suggestions." : "An initial successful database download is required. Use --sample for offline demonstration."; return null; }
        if (!_repository.HasExpensiveData) { error = "Update database to load expensive-item status before printing."; return null; }
        int year = 0, month = 0;
        if (!_yearUnavailable.Checked && (_year.Text.Length != 4 || !int.TryParse(_year.Text, NumberStyles.None, CultureInfo.InvariantCulture, out year))) { error = "Select a reception year."; return null; }
        if (!_monthUnavailable.Checked && !int.TryParse(_month.Text, out month)) { error = "Select a reception month."; return null; }
        if (!decimal.TryParse(_quantity.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) || quantity < 0) { error = "Enter a quantity; use 0 if unavailable."; return null; }
        if (!_decimalQuantity.Checked && quantity != decimal.Truncate(quantity)) { error = "Select Use decimal number to enter a fractional quantity."; return null; }
        return new LabelRequest(c.OepsPn, c.Mpn, month, year, quantity, _monthUnavailable.Checked, _yearUnavailable.Checked, _session.Packaging, _session.RandomCode);
    }
    private void UpdateValidation()
    {
        _printerSettings.Enabled = !_submitting && _printer.SelectedItem is string;
        var isExpensive = _session.Selected?.IsExpensive == true;
        _pn.Text = _session.Selected is { } selected ? selected.OepsPn + (isExpensive ? " - EXPENSIVE ITEM💰" : "") : "—";
        _mpn.Text = _session.Selected?.Mpn ?? "—";
        var description = _session.Selected?.Description ?? "";
        var extended = _extendedDescription.Checked;
        var bracket = description.IndexOf('[');
        var displayedDescription = !extended && bracket >= 0 ? description[..bracket].TrimEnd() : description;
        _description.Font = _mpn.Font;
        _description.Multiline = false; _description.WordWrap = false;
        _description.ScrollBars = ScrollBars.None;
        _descriptionFrame.Padding = new Padding(9, 6, 9, 5);
        if (_description.Text != displayedDescription) _description.Text = displayedDescription;
        var request = GetRequest(out var error);
        try
        {
            var month = int.TryParse(_month.Text, out var m) ? m : 0;
            var year = int.TryParse(_year.Text, out var y) ? y : 0;
            _dateHint.Text = "Print as " + LabelValues.FormatLot(new LabelRequest("", "", month, year, 0, _monthUnavailable.Checked, _yearUnavailable.Checked, _session.Packaging, _session.RandomCode));
        }
        catch (ArgumentException) { _dateHint.Text = ""; }
        if (_templateError is not null) error = _templateError;
        if (isExpensive && _expensiveTemplateError is not null) error = _expensiveTemplateError;
        if (request is not null && _jobRenderer is not null)
        {
            try
            {
                _ = _jobRenderer.Render(request, isExpensive);
                if (!_dryRun.Checked)
                {
                    if (_printer.SelectedItem is not string queue) error = _printerError ?? "Select an installed printer queue.";
                    else if (_config.ExpensivePrinter is not null)
                    {
                        try
                        {
                            _ = _jobRenderer.RenderProfiles(request, _config.Printer, queue, _config.ExpensivePrinter, PrinterTest || isExpensive, !PrinterTest, _settings);
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { error = ex.Message; }
                    }
                    else error = _jobRenderer.ValidateProduction(request, _config.Printer, queue, isExpensive).FirstOrDefault();
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException) { error = e.Message; }
        }
        _validation.Text = error ?? (isExpensive ? (_dryRun.Checked ? "Dry run saves 2 labels: component + expensive item." : "Ready to submit 2 labels: component + expensive item.") : (_dryRun.Checked ? "Dry run saves one label to a ZPL file." : "Ready to submit one sticker."));
        _toolTip.SetToolTip(_validation, _validation.Text);
        _toolTip.SetToolTip(_pn, _pn.Text);
        _toolTip.SetToolTip(_mpn, _session.Selected?.Mpn);
        _toolTip.SetToolTip(_description, displayedDescription);
        _print.Text = _submitting ? "Submitting…" : (_dryRun.Checked ? "&Save dry run" : "&Print sticker") + (isExpensive ? " (2 labels)" : "");
        _print.Enabled = !_submitting && error is null && request is not null && _renderer is not null;
        if (PrinterTest)
        {
            _print.Text = _submitting ? "Submitting…" : "Print two test labels";
            if (error is null) _validation.Text = "Hardware test: L3 first, then L3 expensive.";
        }
        _refresh.Enabled = !_repository.IsRefreshing && !_config.SampleMode;
    }
    private async Task RefreshAsync()
    {
        if (_config.SampleMode) { ShowSuggestions(); UpdateFooter(); return; }
        var refresh = _repository.RefreshAsync(_closing.Token);
        UpdateValidation(); UpdateFooter();
        await refresh;
        if (IsDisposed || _closing.IsCancellationRequested) return;
        if (!_session.Revalidate(_repository.Components)) _jobStatus.Text = "The selected PN/MPN pairing was removed from the database. Select a component again.";
        ShowSuggestions(); UpdateValidation(); UpdateFooter();
    }
    private void UpdateFooter()
    {
        if (_config.SampleMode)
        {
            _syncIndicator.ForeColor = Color.FromArgb(219, 144, 14);
            _syncStatus.Text = "Sample database · offline demo";
            _toolTip.SetToolTip(_syncStatus, "Sample data only. Online refresh and production printing are disabled.");
            return;
        }
        var last = _repository.LastSuccessfulSyncUtc;
        var age = last is null ? "never" : $"{last.Value.LocalDateTime:g} ({Math.Max(0, (int)(DateTimeOffset.UtcNow - last.Value).TotalMinutes)} min ago)";
        var left = _repository.NextRefreshUtc - DateTimeOffset.UtcNow;
        var next = _repository.IsRefreshing ? "refreshing…" : $"next in {Math.Max(0, (int)left.TotalMinutes):00}:{Math.Max(0, left.Seconds):00}";
        _syncIndicator.ForeColor = _repository.LastError is not null ? Color.FromArgb(219, 144, 14) : last is not null ? Color.FromArgb(39, 160, 80) : Color.Gray;
        _syncStatus.Text = _repository.LastError is not null
            ? $"Offline · cache {(last is null ? "unavailable" : Math.Max(0, (int)(DateTimeOffset.UtcNow - last.Value).TotalMinutes) + "m old")} · {next}"
            : last is null ? $"Database not synced · {next}" : $"Database synced {last.Value.LocalDateTime:HH:mm} · {next}";
        _toolTip.SetToolTip(_syncStatus, $"{_repository.Components.Count} pairings. Last successful sync: {age}." + (_repository.LastError is null ? "" : $"\n{_repository.LastError}"));
    }
    private async Task SubmitAsync()
    {
        if (_submitting) return;
        _session.Revalidate(_repository.Components);
        UpdateValidation(); if (!_print.Enabled) return;
        var request = GetRequest(out _)!; var dryRun = _dryRun.Checked; var queue = _printer.SelectedItem as string;
        var isExpensive = PrinterTest || _session.Selected!.IsExpensive;
        if (PrinterTest && !dryRun && MessageBox.Show(this,
            $"Print L3 followed by L3 expensive?\n\nComponent: {request.OepsPn}\nMPN: {request.Mpn}\nL3: {queue}, {_config.Printer.PrintSpeedIps} in/s, darkness {_config.Printer.Darkness}\nL3 expensive: {_config.ExpensivePrinter?.QueueName}, {_config.ExpensivePrinter?.PrintSpeedIps} in/s, darkness {_config.ExpensivePrinter?.Darkness}\nMedia: 30 × 50 mm, ribbon, gaps\n\nThis sends TWO physical labels. Production validation remains disabled.",
            "Confirm printer test", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        _submitting = true; UpdateValidation();
        var acceptedLabels = new List<string>();
        try
        {
            if (dryRun)
            {
                var rendered = _config.ExpensivePrinter is null ? _jobRenderer!.Render(request, isExpensive)
                    : new RenderedPrintJob(_jobRenderer!.RenderProfiles(request, _config.Printer, queue ?? _config.Printer.QueueName ?? "", _config.ExpensivePrinter, isExpensive, false, _settings)
                        .SelectMany(label => label.Bytes).ToArray(), isExpensive ? 2 : 1);
                Directory.CreateDirectory(_paths.DryRunDirectory);
                var file = Path.Combine(_paths.DryRunDirectory, $"label-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zpl");
                await File.WriteAllBytesAsync(file, rendered.Bytes, _closing.Token);
                _jobStatus.Text = $"Saved {rendered.LabelCount} label(s): " + file;
            }
            else
            {
                var labels = _config.ExpensivePrinter is not null
                    ? _jobRenderer!.RenderProfiles(request, _config.Printer, queue!, _config.ExpensivePrinter, isExpensive, !PrinterTest, _settings)
                    : new[] { new RoutedLabel("L3 job", queue!, _jobRenderer!.RenderProduction(request, _config.Printer, queue!, isExpensive).Bytes) };
                foreach (var label in labels)
                {
                    var submission = await new RawPrinterTransport().SendAsync(label.Queue, label.Bytes, _closing.Token);
                    acceptedLabels.Add($"{label.Name}: {label.Queue}, job {submission.JobId}");
                }
                _jobStatus.Text = "Spooler accepted: " + string.Join("; ", acceptedLabels) + ". Check physical output.";
            }
            _jobStatus.Text += " Lot: " + LabelValues.FormatLot(request);
            _session.NextLot();
        }
        catch (Exception e) { _jobStatus.Text = (dryRun ? "Dry run failed: " : "Submission failed or uncertain. Check the printer before retrying: ") + e.Message
            + (acceptedLabels.Count == 0 ? "" : " Already accepted: " + string.Join("; ", acceptedLabels) + ". Retrying prints these again."); }
        finally
        {
            _submitting = false;
            if (!IsDisposed) { _toolTip.SetToolTip(_jobStatus, _jobStatus.Text); UpdateValidation(); }
        }
    }
    private async Task CheckReleaseAsync()
    {
        if (_checkingRelease || _config.SampleMode) { _nextReleaseCheck = DateTimeOffset.UtcNow.AddMinutes(30); return; }
        _checkingRelease = true; _nextReleaseCheck = DateTimeOffset.UtcNow.AddMinutes(30);
        try
        {
            var client = new GitHubReleaseClient(_http, _config.GitHubOwner, _config.GitHubRepository, _config.PackagePrefix);
            var release = await client.GetLatestReleaseAsync(_closing.Token);
            if (!IsDisposed && release is not null && SemanticVersion.TryParse(_version, out var installed) && release.Version.CompareTo(installed) > 0)
            {
                _updateStatus.Text = "Update available";
                _toolTip.SetToolTip(_updateStatus, "Restart this app when ready to install the update. You can choose ‘Not now’ to skip the update.");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException or ArgumentException) { /* Optional release lookup must not disrupt work. */ }
        finally { _checkingRelease = false; }
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_submitting) { e.Cancel = true; _jobStatus.Text = "Wait for the current submission to finish before closing."; return; }
        _timer.Stop(); _closing.Cancel();
        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var scale = DeviceDpi / 96f;
            _settings.WindowWidth = (int)((bounds.Width - (Width - ClientSize.Width)) / scale);
            _settings.WindowHeight = (int)((bounds.Height - (Height - ClientSize.Height)) / scale);
            _settings.WindowX = bounds.X; _settings.WindowY = bounds.Y; _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            _settings.LastPrinterName = _printer.SelectedItem as string ?? _settings.LastPrinterName;
            _settings.SearchMode = _oepsMode.Checked ? SearchMode.OepsPn : SearchMode.Mpn;
            _settings.ExtendedDescription = _extendedDescription.Checked;
            _settings.Save(_paths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "Could not save preferences: " + ex.Message, Text); }
    }
    private async Task RunUiSmokeAsync()
    {
        _timer.Stop();
        var report = new List<string>();
        try
        {
            if (!_config.SampleMode) throw new InvalidOperationException("UI smoke requires --sample.");
            _printer.Items.Add("Zebra ZD421 (sample)");
            _printer.SelectedItem = "Zebra ZD421 (sample)";
            if (!_printerSettings.Enabled || _printerSettings.Parent != _printer.Parent || _printer.Right >= _printerSettings.Left)
                throw new Exception("Printer settings button is not alongside the queue selector.");
            if (!_printer.Parent!.ClientRectangle.Contains(_printer.Bounds) || !_printerSettings.Parent!.ClientRectangle.Contains(_printerSettings.Bounds))
                throw new Exception("Printer selector or settings button is clipped.");
            using (var offsetsDialog = new PrinterOffsetsDialog(_printer.Text, new PrinterOffsets(1.25m, -0.5m)) { Opacity = 0 })
            {
                offsetsDialog.Shown += (_, _) =>
                {
                    using var snapshot = new Bitmap(offsetsDialog.Width, offsetsDialog.Height);
                    offsetsDialog.DrawToBitmap(snapshot, new Rectangle(Point.Empty, snapshot.Size));
                    Directory.CreateDirectory(_paths.UserDataRoot);
                    snapshot.Save(Path.Combine(_paths.UserDataRoot, "ui-smoke-printer-offsets.png"));
                    offsetsDialog.DialogResult = DialogResult.Cancel;
                    offsetsDialog.Close();
                };
                offsetsDialog.ShowDialog(this);
                if (offsetsDialog.Offsets != new PrinterOffsets(1.25m, -0.5m)) throw new Exception("Offset dialog did not load the saved values.");
            }
            report.Add("PASS printer settings button, offset dialog and loaded millimetre values");
            _search.Text = "OEPS101234";
            if (_suggestions.Items.Count < 2) throw new Exception("Multiple MPN suggestions missing.");
            if (!_suggestions.Visible) throw new Exception("Typing did not open the autocomplete dropdown.");
            SearchKeyDown(_search, new KeyEventArgs(Keys.Down));
            if (!_suggestions.Visible || Controls.GetChildIndex(_suggestions) != 0 || !ClientRectangle.Contains(_suggestions.Bounds))
                throw new Exception("Autocomplete is not above the form fields and within the window.");
            Directory.CreateDirectory(_paths.UserDataRoot);
            using (var autocomplete = CaptureWindow()) autocomplete.Save(Path.Combine(_paths.UserDataRoot, "ui-smoke-autocomplete.png"));
            SearchKeyDown(_search, new KeyEventArgs(Keys.Escape));
            if (_suggestions.Visible || _session.Selected is not null) throw new Exception("Escape did not dismiss the suggestions without selection.");
            SearchKeyDown(_search, new KeyEventArgs(Keys.Down));
            SearchKeyDown(_search, new KeyEventArgs(Keys.Enter));
            if (_suggestions.Visible) throw new Exception("Selecting a component did not close the dropdown.");
            report.Add("PASS autocomplete opens while typing, supports arrows/Enter, and dismisses with Escape or selection");
            _year.Text = "2026"; _month.SelectedItem = "09"; _quantity.Text = "42";
            if (_decimalQuantity.Checked || _quantity.DecimalPlaces != 0 || _quantity.Text.Contains('.') || _quantity.Text.Contains(','))
                throw new Exception("Quantity must default to whole numbers without a decimal suffix.");
            _decimalQuantity.Checked = true;
            _quantity.Value = 12.345m;
            if (_quantity.DecimalPlaces != 3 || GetRequest(out _)?.Quantity != 12.345m)
                throw new Exception("Decimal mode did not pass the fractional quantity to printing.");
            _decimalQuantity.Checked = false;
            if (_quantity.DecimalPlaces != 0 || _quantity.Value != 12m || GetRequest(out _)?.Quantity != 12m)
                throw new Exception("Disabling decimal mode retained a hidden fractional quantity.");
            _quantity.Value = 42;
            report.Add("PASS whole-number default and decimal quantity toggle without hidden fractions");
            if (!_print.Enabled) throw new Exception("Valid sample dry run is disabled: " + _validation.Text);
            if (_pn.Text != "OEPS101234 - EXPENSIVE ITEM💰") throw new Exception("The expensive-item suffix is missing from the OEPS PN display.");
            if (_jobRenderer!.Render(GetRequest(out _)!, _session.Selected!.IsExpensive).LabelCount != 2) throw new Exception("An expensive component must generate two labels.");
            report.Add("PASS expensive-item display suffix and two-label dry-run job");
            await SubmitAsync();
            if (!Directory.EnumerateFiles(_paths.DryRunDirectory, "*.zpl").Any()) throw new Exception("Dry run file missing.");
            report.Add("PASS explicit pairing selection, lot/quantity entry and dry-run file generation");
            _packaging.SelectedItem = "Tray";
            var lotBeforeRefresh = LabelValues.FormatLot(GetRequest(out _)!);
            await RefreshAsync();
            if (_year.Text != "2026" || _month.Text != "09" || _quantity.Value != 42 || _packaging.Text != "Tray"
                || _dateHint.Text != "Print as " + lotBeforeRefresh || _renderer!.Render(GetRequest(out _)!).LotCode != lotBeforeRefresh)
                throw new Exception("Refresh erased entries or lot preview differs from the label.");
            if (_month.Width != _year.Width || _monthUnavailable.Left != _month.Left || _yearUnavailable.Left != _year.Left
                || _monthUnavailable.CheckAlign != ContentAlignment.MiddleLeft || _yearUnavailable.CheckAlign != ContentAlignment.MiddleLeft)
                throw new Exception("Year width or unavailable checkbox alignment is incorrect.");
            if (!_year.Items.Cast<string>().SequenceEqual(Enumerable.Range(2020, DateTime.Now.Year - 2020 + 1).Select(y => y.ToString("0000"))))
                throw new Exception("Year options must run from 2020 to the current year.");
            report.Add("PASS packaging selection, stable lot across refresh and matching printed QR/preview");
            _search.Text += "x";
            if (_session.Selected is not null || _print.Enabled) throw new Exception("Search edit retained stale selection.");
            report.Add("PASS refresh preserves form entries; editing search invalidates selection");
            if (_dryRun.Enabled) throw new Exception("Sample mode permits production.");
            report.Add("PASS sample production lock");
            _search.Text = "OEPS101234"; ShowSuggestions(); _suggestions.SelectedIndex = 0; SelectSuggestion();
            if (!_dateHint.Text.StartsWith("Print as 0926_")) throw new Exception("Inline reception date hint is inconsistent.");
            _monthUnavailable.Checked = true;
            await RefreshAsync();
            if (!_monthUnavailable.Checked || _month.Enabled || !_year.Enabled || !_dateHint.Text.StartsWith("Print as 0026_") || !_print.Enabled
                || _renderer!.Render(GetRequest(out _)!).DateMmyy != "0026")
                throw new Exception("Month-only unavailability must print 00YY and survive refresh.");
            _yearUnavailable.Checked = true;
            if (_month.Enabled || _year.Enabled || !_dateHint.Text.StartsWith("Print as 0000_"))
                throw new Exception("Both unavailable fields must print 0000.");
            if (_renderer!.Render(GetRequest(out _)!).DateMmyy != "0000") throw new Exception("Unavailable date did not reach the renderer.");
            await SubmitAsync();
            _monthUnavailable.Checked = false;
            await RefreshAsync();
            if (!_month.Enabled || _year.Enabled || !_yearUnavailable.Checked || !_dateHint.Text.StartsWith("Print as 0900_") || !_print.Enabled
                || _renderer!.Render(GetRequest(out _)!).DateMmyy != "0900")
                throw new Exception("Year-only unavailability must print MM00 and survive refresh.");
            _yearUnavailable.Checked = false;
            if (!_month.Enabled || !_year.Enabled || _month.Text != "09" || _year.Text != "2026" || !_dateHint.Text.StartsWith("Print as 0926_"))
                throw new Exception("Toggling date availability did not preserve the entered date.");
            report.Add("PASS separate month/year zero checkboxes, preserved selections and printed 00YY/MM00/0000");
            var originalSelection = _session.Selected!;
            _session.Select(originalSelection with { Description = "Crystal [package information] [stock]" });
            UpdateValidation();
            if (_description.Text != "Crystal" || _description.Font.SizeInPoints != _mpn.Font.SizeInPoints || _description.Multiline)
                throw new Exception("Short description must stop before the first bracket and match the identifier font.");
            _extendedDescription.Checked = true;
            if (_description.Text != "Crystal [package information] [stock]" || !_description.Font.Equals(_mpn.Font) || _description.Multiline || _description.ScrollBars != ScrollBars.None)
                throw new Exception("Extended description must preserve the complete text in a single line without scrollbars and match the identifier font.");
            _extendedDescription.Checked = false;
            _session.Select(originalSelection); UpdateValidation();
            report.Add("PASS short/full description toggle, bracket trimming and exact font sizes");
            _jobStatus.Text = "";
            using var screenshot = CaptureWindow();
            Directory.CreateDirectory(_paths.UserDataRoot); screenshot.Save(Path.Combine(_paths.UserDataRoot, "ui-smoke.png"));
            ClientSize = new Size((int)(560 * DeviceDpi / 96f), (int)(UserSettings.MinimumWindowHeight * DeviceDpi / 96f));
            PerformLayout();
            var footerTop = _syncStatus.Parent!.Top;
            foreach (var status in new[] { _validation, _jobStatus })
                if (PointToClient(status.PointToScreen(new Point(0, status.Height))).Y > footerTop)
                    throw new Exception("Status or report is clipped below the footer at minimum window height.");
            var hintSize = TextRenderer.MeasureText(_dateHint.Text, _dateHint.Font,
                new Size(_dateHint.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
            if (hintSize.Height > _dateHint.ClientSize.Height) throw new Exception("Lot preview is clipped at minimum window width.");
            using var minimumScreenshot = CaptureWindow();
            minimumScreenshot.Save(Path.Combine(_paths.UserDataRoot, "ui-smoke-minimum.png"));
            report.Add("PASS complete lot preview fits the minimum window width");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { Directory.CreateDirectory(_paths.UserDataRoot); await File.WriteAllLinesAsync(Path.Combine(_paths.UserDataRoot, "ui-smoke.txt"), report); Close(); }
    }
    private Bitmap CaptureWindow()
    {
        var screenshot = new Bitmap(Width, Height);
        DrawToBitmap(screenshot, new Rectangle(0, 0, Width, Height));
        // DrawToBitmap does not preserve overlapping child-window paint order. Capture the
        // visible native dropdown separately at its actual client position for smoke artifacts.
        if (_suggestions.Visible)
        {
            using var dropdown = new Bitmap(_suggestions.Width, _suggestions.Height);
            _suggestions.DrawToBitmap(dropdown, new Rectangle(Point.Empty, dropdown.Size));
            var clientOffset = PointToScreen(Point.Empty);
            using var graphics = Graphics.FromImage(screenshot);
            graphics.DrawImageUnscaled(dropdown, _suggestions.Left + clientOffset.X - Left, _suggestions.Top + clientOffset.Y - Top);
        }
        return screenshot;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _toolTip.Dispose(); _closing.Cancel(); _closing.Dispose(); }
        base.Dispose(disposing);
    }
    private sealed record Suggestion(Component Component)
    {
        public string DisplayOepsPn => Component.OepsPn + (Component.IsExpensive ? " 💰" : "");
        public override string ToString() => $"{DisplayOepsPn}   |   {Component.Mpn}";
    }
}
