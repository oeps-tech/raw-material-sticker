using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.App;

internal sealed class PrinterOffsetsDialog : Form
{
    private readonly NumericUpDown _x = OffsetInput("Horizontal offset in millimetres");
    private readonly NumericUpDown _y = OffsetInput("Vertical offset in millimetres");
    public PrinterOffsets Offsets => new(_x.Value, _y.Value);

    public PrinterOffsetsDialog(string queue, PrinterOffsets offsets)
    {
        SuspendLayout();
        Text = "Printer offsets";
        Font = new Font("Segoe UI", 10f);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        _x.Value = Math.Clamp(offsets.XMm, -10, 10); _y.Value = Math.Clamp(offsets.YMm, -10, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        foreach (var height in new[] { 48, 36, 36, 58, 40 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var printer = new Label { Text = queue, Dock = DockStyle.Fill, AutoEllipsis = true };
        layout.Controls.Add(printer, 0, 0); layout.SetColumnSpan(printer, 2);
        layout.Controls.Add(new Label { Text = "X offset (mm)", AutoSize = true, Margin = new Padding(0, 5, 0, 0) }, 0, 1);
        layout.Controls.Add(_x, 1, 1);
        layout.Controls.Add(new Label { Text = "Y offset (mm)", AutoSize = true, Margin = new Padding(0, 5, 0, 0) }, 0, 2);
        layout.Controls.Add(_y, 1, 2);
        var hint = new Label { Text = "X: positive moves right; negative moves left.\nY: positive moves down; negative moves up.\nApplies to both labels on this printer.", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9f) };
        layout.Controls.Add(hint, 0, 3); layout.SetColumnSpan(hint, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        var reset = new Button { Text = "Reset to zero", AutoSize = true };
        save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        reset.Click += (_, _) => { _x.Value = _y.Value = 0; };
        buttons.Controls.AddRange([cancel, save, reset]);
        layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        ResumeLayout(true);
    }

    private static NumericUpDown OffsetInput(string name) => new()
    {
        Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.1m,
        Dock = DockStyle.Top, AccessibleName = name, ThousandsSeparator = false
    };
}
