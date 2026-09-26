using System.Globalization;
using Oeps.RawMaterialSticker.Core.Camera;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.App;

internal sealed partial class CameraForm : Form
{
    private readonly ICameraConnection _connection;
    private readonly Func<CameraReading, Component> _resolve;
    private readonly Func<CameraReading, decimal, Task<string>> _printLabel;
    private readonly Action<CameraReading, decimal> _updateFields;
    private readonly Action _readingFeedback;
    private readonly ScannerReadSound? _readSound;
    private readonly CameraInbox _inbox = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TextBox _pn = DisplayBox("Camera OEPS PN");
    private readonly TextBox _mpn = DisplayBox("Camera MPN");
    private readonly TextBox _lot = DisplayBox("Camera lot");
    private readonly QuantityInput _quantity = new() { Minimum = 0, Maximum = 9999999999m, DecimalPlaces = 9, Dock = DockStyle.Fill, AccessibleName = "Camera quantity" };
    private readonly Button _print = new() { Text = "Update quantity and print (Enter)", AutoSize = true };
    private readonly Button _update = new() { Text = "Update fields (Space)", AutoSize = true };
    private readonly Button _leave = new() { Text = "Leave (Escape)", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label _printStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 30000 };
    private readonly Label _connectionStatus = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoEllipsis = true };
    private CameraReading? _reading;
    private Component? _component;
    private Task _connecting = Task.CompletedTask;
    private bool _busy, _closing, _allowClose;
    private string? _pendingJson;

    public CameraForm(string? ip, int port, bool local, Func<CameraReading, Component> resolve,
        Func<CameraReading, decimal, Task<string>> printLabel, Action<CameraReading, decimal> updateFields,
        ICameraConnection? connection = null, Action? readingFeedback = null)
    {
        SuspendLayout();
        _connection = connection ?? new CameraConnection(); _resolve = resolve; _printLabel = printLabel; _updateFields = updateFields;
        if (readingFeedback is null)
        {
            _readSound = new ScannerReadSound(onError: error => Post(() => _connectionStatus.Text = "Scanner sound unavailable: " + error));
            _readingFeedback = _readSound.Play;
        }
        else _readingFeedback = readingFeedback;
        Text = "Update fields from camera"; Font = new Font("Segoe UI", 10f);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(530, 400); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MaximizeBox = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var inputs = new Control[] { _pn, _mpn, _lot, _quantity };
        var names = new[] { "OEPS PN", "MPN", "Lot", "Quantity" };
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i].Font = new Font(Font.FontFamily, 13f);
            layout.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            inputs[i].Margin = new Padding(0, 5, 0, 5); layout.Controls.Add(inputs[i], 1, i);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.AddRange([_print, _update, _leave]); layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(_status, 0, 5); layout.SetColumnSpan(_status, 2);
        layout.Controls.Add(_printStatus, 0, 6); layout.SetColumnSpan(_printStatus, 2);
        layout.Controls.Add(_connectionStatus, 0, 7); layout.SetColumnSpan(_connectionStatus, 2);
        Controls.Add(layout);
        // Fit the action row with the same small margin on either side.
        ClientSize = new Size(new[] { _print, _update, _leave }.Sum(button => button.GetPreferredSize(Size.Empty).Width + button.Margin.Horizontal) + layout.Padding.Horizontal, ClientSize.Height);
        _status.Text = "Waiting for a camera reading…";
        _print.Click += async (_, _) => await PrintAsync();
        _update.Click += (_, _) => UpdateFields();
        _leave.Click += (_, _) => { _inbox.ActionPressed(); Close(); };
        _quantity.TextChanged += (_, _) => ValidateQuantity();
        _quantity.ValueChanged += (_, _) => ValidateQuantity();
        foreach (var status in new[] { _status, _printStatus, _connectionStatus })
            status.TextChanged += (_, _) => _toolTip.SetToolTip(status, status.Text);
        _connection.JsonReceived += Receive;
        _connection.StateChanged += StateChanged;
        Shown += (_, _) => _connecting = ConnectAsync(ip, port, local);
        FormClosing += ClosingAsync;
        ValidateQuantity(); ResumeLayout(true);
    }

    private static TextBox DisplayBox(string name) => new() { ReadOnly = true, Dock = DockStyle.Fill, TabStop = false, AccessibleName = name };
    private void Post(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!_closing && !IsDisposed) action(); }); }
        catch (InvalidOperationException) { }
    }
    private void StateChanged(string state) => Post(() => _connectionStatus.Text = state);
    private async Task ConnectAsync(string? ip, int port, bool local)
    {
        _connectionStatus.Text = $"Connecting to {(local ? "127.0.0.1" : ip)}:{port}…";
        try { await _connection.ConnectAsync(ip, port, local, _stop.Token); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception e) { if (!_closing) _connectionStatus.Text = "Connection failed: " + e.Message + " Leave and reopen to retry."; }
    }
    private void Receive(string json) => Post(() =>
    {
        if (!_inbox.Accept(json)) return;
        if (_busy) { _pendingJson = json; return; }
        Display(json);
    });
    private void Display(string json)
    {
        _reading = null; _component = null; _pn.Clear(); _mpn.Clear(); _lot.Clear(); _quantity.Text = "";
        try
        {
            _reading = CameraReading.Parse(json);
            _pn.Text = _reading.OepsPn; _lot.Text = _reading.Lot; _quantity.Value = _reading.Quantity;
            _quantity.Text = _reading.Quantity.ToString("0.#########", CultureInfo.CurrentCulture);
            _component = _resolve(_reading); _mpn.Text = _component.Mpn;
            _status.Text = _component.IsExpensive ? "Expensive item: printing will produce L3 and L3 expensive." : "Update Quantity, then choose an action.";
        }
        catch (Exception e) { _status.Text = e.Message; }
        ValidateQuantity();
        if (_reading is not null) { _quantity.Focus(); _quantity.Select(0, _quantity.Text.Length); }
        if (_component is not null)
        {
            try { _readingFeedback(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Camera reading sound: " + ex); }
        }
    }
    private decimal? ValidQuantity()
    {
        if (!decimal.TryParse(_quantity.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)) return null;
        try { _ = LabelValues.FormatQuantityDataMatrix(quantity); return quantity; }
        catch (ArgumentException) { return null; }
    }
    private void ValidateQuantity()
    {
        _quantity.Enabled = !_busy && !_closing && _reading is not null;
        _print.Enabled = _update.Enabled = !_busy && !_closing && _component is not null && ValidQuantity() is not null;
        if (_component is not null && ValidQuantity() is null) _status.Text = "Enter a quantity greater than zero, with at most 10 digits.";
    }
    private async Task PrintAsync()
    {
        _inbox.ActionPressed();
        if (!_print.Enabled || _reading is null || ValidQuantity() is not decimal quantity) return;
        var reading = _reading;
        _busy = true; ValidateQuantity();
        try
        {
            _printStatus.ForeColor = SystemColors.ControlText;
            _printStatus.Text = "Submitting labels…";
            _printStatus.Text = await _printLabel(reading, quantity);
            _reading = null; _component = null; _pn.Clear(); _mpn.Clear(); _lot.Clear(); _quantity.Text = "";
            _status.Text = "Waiting for the next camera reading…";
        }
        catch (Exception e) { _printStatus.ForeColor = Color.Firebrick; _printStatus.Text = e.Message; }
        finally
        {
            _busy = false;
            if (_pendingJson is { } pending) { _pendingJson = null; Display(pending); }
            ValidateQuantity();
        }
    }
    private void UpdateFields()
    {
        _inbox.ActionPressed();
        if (!_update.Enabled || _reading is null || ValidQuantity() is not decimal quantity) return;
        try { _updateFields(_reading, quantity); Close(); }
        catch (Exception e) { _status.Text = e.Message; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Holding Enter must not submit again when another reading arrives.
        if (keyData is Keys.Enter or Keys.Space or Keys.Escape && (msg.LParam.ToInt64() & (1L << 30)) != 0) return true;
        if (keyData == Keys.Enter) { if (!_busy) _print.PerformClick(); return true; }
        if (keyData == Keys.Space) { if (!_busy) _update.PerformClick(); return true; }
        if (keyData == Keys.Escape) { _inbox.ActionPressed(); Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_busy) { _status.Text = "Wait for the current print submission to finish."; return; }
        if (_closing) return;
        _closing = true; _stop.Cancel(); ValidateQuantity(); _leave.Enabled = false;
        _connection.JsonReceived -= Receive; _connection.StateChanged -= StateChanged;
        _connectionStatus.Text = "Disconnecting…";
        // Complete the cancelled FormClosing event before Close is called again, even
        // when the SDK closes synchronously (for example after a failed connection).
        await Task.Yield();
        try { await _connecting; await _connection.CloseAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Camera close: " + ex); }
        finally
        {
            try { await _connection.DisposeAsync(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Camera dispose: " + ex); }
            finally { _allowClose = true; _stop.Dispose(); Close(); }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _toolTip.Dispose(); _readSound?.Dispose(); }
        base.Dispose(disposing);
    }
}
