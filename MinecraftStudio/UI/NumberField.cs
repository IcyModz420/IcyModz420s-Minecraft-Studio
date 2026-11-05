// MinecraftStudio/UI/NumberField.cs: Whole-number field (digits only; Up/Down step the value) in the same frame
// as DarkTextField.
// Owns: NumberField.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Whole-number field (digits only; Up/Down step the value) in the same frame as <see cref="DarkTextField"/>.</summary>
sealed class NumberField : DarkTextField
{
    decimal _value;
    bool _syncing;

    public NumberField()
    {
        Box.TextAlign = HorizontalAlignment.Right;
        Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && Minimum < 0)) e.Handled = true; };
        Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Value = Math.Min(Maximum, Value + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Value = Math.Max(Minimum, Value - 1); e.Handled = true; }
        };
        Box.TextChanged += (_, _) =>
        {
            if (_syncing || !decimal.TryParse(Box.Text, out var v)) return;
            v = Math.Clamp(v, Minimum, Maximum);
            if (v != _value) { _value = v; ValueChanged?.Invoke(this, EventArgs.Empty); }
        };
        Box.Leave += (_, _) => Value = _value;
    }

    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public event EventHandler? ValueChanged;

    public decimal Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            bool changed = value != _value;
            _value = value;
            _syncing = true;
            Box.Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _syncing = false;
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
