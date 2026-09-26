using System.Globalization;

namespace Oeps.RawMaterialSticker.App;

internal sealed class QuantityInput : NumericUpDown
{
    private bool _formatting;
    protected override void UpdateEditText()
    {
        if (_formatting) return;
        _formatting = true;
        try
        {
            base.UpdateEditText();
            // Value can validate pending edits and re-enter UpdateEditText.
            // Keep that validation from recursively formatting the same input.
            if (DecimalPlaces > 0) Text = Value.ToString("0.#########", CultureInfo.CurrentCulture);
        }
        finally { _formatting = false; }
    }
}
