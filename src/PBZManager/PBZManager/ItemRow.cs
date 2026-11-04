using System.ComponentModel;
using System.Runtime.CompilerServices;
using PBZ.Core.Items;

namespace PBZManager;

// One row of the items page. NewAmount is what the user typed; empty means "leave as is".
public sealed class ItemRow : INotifyPropertyChanged
{
    private string _newAmount = "";

    public ItemRow(InventoryItem item) => Item = item;

    public InventoryItem Item { get; }
    public string Category => Item.Category;
    public string Name => string.IsNullOrWhiteSpace(Item.Name) ? Item.Id : Item.Name!;
    public string Id => Item.Id;
    public string Amount => Item.Amount?.ToString() ?? "?";
    public string HasMax => Item.HasMax?.ToString() ?? "?";
    public bool Editable => Item.Editable;

    public string NewAmount { get => _newAmount; set => Set(ref _newAmount, value.Trim()); }

    // Null when nothing to apply; throws FormatException for text that is not a whole number.
    public long? Target()
    {
        if (!Editable || NewAmount.Length == 0) return null;
        if (!long.TryParse(NewAmount, out var v)) throw new FormatException($"“{Name}”的新数量不是整数");
        v = Math.Clamp(v, 0, InventorySnapshot.ItemMax);
        return v == Item.Amount ? null : v;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
