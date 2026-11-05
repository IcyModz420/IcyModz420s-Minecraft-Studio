// MinecraftStudio/Documents/WorldPages/PlayersPage.Editing.cs: editing for PlayersPage.
// Part of PlayersPage (main part in PlayersPage.cs).
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

partial class PlayersPage
{
    // ------------------------------------------------------------------ grid

    void BuildGrid()
    {
        _grid.SuspendLayout();
        foreach (var c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
        _slots.Clear();
        if (_inv == null) { _grid.ResumeLayout(); return; }
        const int Cell = 58;
        int y = 0;
        void Label(string text, int x, int yy) => _grid.Controls.Add(new Label { Text = text, Location = P(x, yy), AutoSize = true, ForeColor = C.FG_FAINT, Font = Theme.Ui(8f, FontStyle.Bold), UseMnemonic = false });
        void Slot(InventoryPart part, int index, int x, int yy, string name)
        {
            var s = new SlotView { Location = P(x, yy), Size = S(Cell - 4, Cell - 4), AccessibleName = name, SlotName = name };
            s.Click += (_, _) => SelectSlot(part, index);
            s.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) SlotMenu(part, index, s, e.Location); };
            _slots[(part, index)] = s;
            _grid.Controls.Add(s);
            Refresh(part, index);
        }
        // Armour and off-hand down the left, like the game.
        Label("ARMOUR", 0, y);
        for (int i = 0; i < 4; i++) Slot(InventoryPart.Armor, i, 0, y + 18 + i * Cell, PlayerInventory.ArmorSlots[i]);
        if (_inv.HasOffhand)
        {
            Label("OFF-HAND", Cell + 14, y + 18 + 3 * Cell - 18);
            Slot(InventoryPart.Offhand, 0, Cell + 14, y + 18 + 3 * Cell, "Off-hand");
        }
        y += 18 + 4 * Cell + 12;
        Label("INVENTORY", 0, y);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 9; c++) Slot(InventoryPart.Storage, r * 9 + c, c * Cell, y + 18 + r * Cell, $"Inventory {r * 9 + c + 1}");
        y += 18 + 3 * Cell + 8;
        Label("HOTBAR", 0, y);
        for (int c = 0; c < 9; c++) Slot(InventoryPart.Hotbar, c, c * Cell, y + 18, $"Hotbar {c + 1}");
        y += 18 + Cell + 12;
        if (_inv.HasEnderChest)
        {
            Label("ENDER CHEST", 0, y);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 9; c++) Slot(InventoryPart.EnderChest, r * 9 + c, c * Cell, y + 18 + r * Cell, $"Ender chest {r * 9 + c + 1}");
            y += 18 + 3 * Cell + 8;
        }
        _grid.Size = S(9 * Cell, y);
        _grid.ResumeLayout();
    }

    void Refresh(InventoryPart part, int index)
    {
        if (!_slots.TryGetValue((part, index), out var view) || _inv == null) return;
        var item = _inv.Get(part, index);
        view.Item = item;
        view.Icon = item == null ? null : ItemIcons.For(DisplayId(item));
        view.Label = item == null ? "" : Pretty(DisplayId(item));
        view.Selected = _selected == (part, index);
        string tip = item == null ? view.SlotName + ": empty" : $"{view.SlotName}: {Pretty(DisplayId(item))} ×{item.Count}" + (item.CustomName != null ? $" “{item.CustomName}”" : "") +
            (item.Enchantments.Count > 0 ? "\n" + string.Join(", ", item.Enchantments.Select(e => $"{Pretty(e.Name)} {e.Level}")) : "") + (item.Damage > 0 ? $"\ndamage {item.Damage}" : "");
        view.Tip = tip;
        view.Invalidate();
    }

    /// <summary>The id to show (a numeric id shows as its name).</summary>
    string DisplayId(InventoryItem item) => _inv!.NumericIds && int.TryParse(item.Id, out int n) ? PlayerInventory.NameOfNumber(n) ?? item.Id : item.Id;

    /// <summary>Right-click on a slot: the item as a /give command (Java and Bedrock) or as SNBT, to paste anywhere.</summary>
    void SlotMenu(InventoryPart part, int index, Control at, Point where)
    {
        if (_inv == null) return;
        SelectSlot(part, index);
        if (_inv.Get(part, index) is not InventoryItem item || item.IsEmpty) return;
        var nbt = _inv.WriteItem(item);
        nbt.Remove("Slot");
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        bool commands = World is JavaWorld or Core.Bedrock.BedrockWorld;
        var give = commands ? GiveCommand.For(nbt, _inv.Format, World is Core.Bedrock.BedrockWorld) : null;
        menu.Items.Add("Copy /give command", null, (_, _) =>
        {
            if (give == null) return;
            Copy(give.Command, give.Note ?? "Copied the /give command — paste it in chat, a command block or a function.");
        }).Enabled = give != null;
        menu.Items.Add("Copy the item as SNBT", null, (_, _) => Copy(Core.Nbt.Snbt.Write(nbt, Core.Nbt.SnbtStyle.Compact), "Copied the item as SNBT, as the world saves it."));
        menu.Items.Add("Edit the item as SNBT…", null, (_, _) => EditAsSnbt(part, index, nbt));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(at, where);
    }

    /// <summary>
    /// The whole item as the world saves it, edited as text (components, lore, attributes … anything the item can hold);
    /// read back as an item of this format and put in the slot. Saved with the page's Save (after a backup), like any edit.
    /// </summary>
    void EditAsSnbt(InventoryPart part, int index, Core.Nbt.NbtCompound nbt)
    {
        if (_inv == null) return;
        string? text = Dialogs.Input(FindForm()!, "Edit the item as SNBT", $"The item in {_slots[(part, index)].SlotName}, exactly as the world saves it. Change anything; it is read back when you press OK.",
            Core.Nbt.Snbt.Write(nbt, Core.Nbt.SnbtStyle.Pretty), multiline: true);
        if (text == null) return;
        try
        {
            if (Core.Nbt.Snbt.Parse(text) is not Core.Nbt.NbtCompound edited) throw new FormatException("An item is a compound: { … }.");
            edited.Remove("Slot");
            var item = _inv.ReadItem(edited);
            if (item.IsEmpty) throw new FormatException("That is not an item any more (it needs an id and a count of at least 1). To empty the slot, use Empty slot.");
            _inv.Set(part, index, item);
            MarkDirty();
            Refresh(part, index);
            UpdateEditor();
            _status.Text = "Unsaved changes: the item was read back from the SNBT.";
        }
        catch (Exception ex) when (ex is Core.Nbt.SnbtException or FormatException or InvalidDataException or ArgumentException or InvalidCastException or IndexOutOfRangeException)
        {
            Dialogs.Error(FindForm()!, "That SNBT could not be read as an item:\n\n" + ex.Message);
        }
    }

    void Copy(string text, string said)
    {
        try
        {
            Clipboard.SetText(text);
            _status.ForeColor = C.FG_DIM;
            _status.Text = said;
        }
        catch (System.Runtime.InteropServices.ExternalException) { _status.ForeColor = C.ORANGE; _status.Text = "The clipboard is busy; try again."; }
    }

    void SelectSlot(InventoryPart part, int index)
    {
        // Apply what is being typed in the slot that is left.
        if (_selected != null) Edited();
        var old = _selected;
        _selected = (part, index);
        if (old is var (op, oi)) Refresh(op, oi);
        Refresh(part, index);
        UpdateEditor();
    }

    // ------------------------------------------------------------------ editor

    void UpdateEditor()
    {
        _loading = true;
        var item = _selected is var (p, i) && _inv != null ? _inv.Get(p, i) : null;
        bool on = _inv != null && _selected != null;
        bool canEdit = on && PlayerStore.CanSave(World);
        _slotTitle.Text = !on ? (_inv == null ? "Choose a player" : "Choose a slot") : _slots[_selected!.Value].SlotName;
        _id.Text = item == null ? "" : _inv!.NumericIds && int.TryParse(item.Id, out int n) && PlayerInventory.NameOfNumber(n) is string name ? $"{n} ({name})" : item.Id;
        _count.Value = Math.Clamp(item?.Count ?? 1, (int)_count.Minimum, (int)_count.Maximum);
        _damage.Value = Math.Clamp(item?.Damage ?? 0, 0, 32767);
        _data.Value = Math.Clamp(item?.Data ?? 0, 0, 32767);
        _dataLabel.Visible = _data.Visible = _inv?.SeparateData == true;
        _name.Text = item?.CustomName ?? "";
        _enchantments.Items.Clear();
        foreach (var e in item?.Enchantments ?? [])
            _enchantments.Items.Add(new ListViewItem(Pretty(e.Name)) { Tag = e }).SubItems.Add(e.Level.ToString());
        // The enchantments list stays enabled (a disabled list is drawn light); adding and removing follow the item.
        foreach (var c in new Control[] { _id, _count, _damage, _data, _name, _enchantName, _enchantLevel, _addEnchant, _removeEnchant, _clear })
            c.Enabled = canEdit && (item != null || c == _id);
        _loading = false;
    }

    /// <summary>Applies the editor's fields to the selected slot (a new item when an id is typed into an empty slot).</summary>
    void Edited()
    {
        if (_loading || _inv == null || _selected is not var (part, index) || !PlayerStore.CanSave(World)) return;
        var current = _inv.Get(part, index);
        string id = ParseId(_id.Text.Trim());
        if (id.Length == 0)
        {
            if (current != null) { _id.Text = current.Id; }
            return;
        }
        var item = current?.Clone() ?? new InventoryItem();
        item.Id = id;
        item.Count = (int)_count.Value;
        item.Damage = (int)_damage.Value;
        item.Data = (int)_data.Value;
        item.CustomName = _name.Text.Length > 0 ? _name.Text : null;
        if (current == null && _inv.NumericIds == false && !id.Contains(':')) item.Id = "minecraft:" + id;
        if (Same(current, item)) return;
        _inv.Set(part, index, item);
        MarkDirty();
        Refresh(part, index);
        UpdateEditor();
    }

    static bool Same(InventoryItem? a, InventoryItem b) =>
        a != null && a.Id == b.Id && a.Count == b.Count && a.Damage == b.Damage && a.Data == b.Data && a.CustomName == b.CustomName && a.Enchantments.SequenceEqual(b.Enchantments);

    /// <summary>The id to store: numeric formats take a number, or a name looked up in the old id table.</summary>
    string ParseId(string text)
    {
        if (text.Length == 0) return "";
        if (_inv!.NumericIds)
        {
            string head = text.Split(' ')[0];
            if (int.TryParse(head, out int n)) return n.ToString();
            string key = text.Contains(':') ? text : "minecraft:" + text;
            return JavaBlockData.LegacyItemIds.TryGetValue(key, out int id) ? id.ToString() : "";
        }
        return text.Split(' ')[0];
    }

    void AddEnchantment()
    {
        if (_inv == null || _selected is not var (part, index) || _inv.Get(part, index) is not InventoryItem current) return;
        if ((_enchantName.SelectedItem as ComboItem)?.Tag is not string name) return;
        var item = current.Clone();
        item.Enchantments.RemoveAll(e => e.Name == name);
        item.Enchantments.Add(new(name, (int)_enchantLevel.Value));
        if (item.Id.EndsWith("enchanted_book", StringComparison.Ordinal) && _inv.Format != ItemFormat.Bedrock) item.StoredEnchantments = true;
        _inv.Set(part, index, item);
        MarkDirty();
        Refresh(part, index);
        UpdateEditor();
    }

    void RemoveEnchantment()
    {
        if (_inv == null || _selected is not var (part, index) || _inv.Get(part, index) is not InventoryItem current || _enchantments.SelectedItems.Count == 0) return;
        var e = (Enchantment)_enchantments.SelectedItems[0].Tag!;
        var item = current.Clone();
        item.Enchantments.Remove(e);
        _inv.Set(part, index, item);
        MarkDirty();
        Refresh(part, index);
        UpdateEditor();
    }

    void ClearSlot()
    {
        if (_inv == null || _selected is not var (part, index) || _inv.Get(part, index) == null) return;
        _inv.Set(part, index, null);
        MarkDirty();
        Refresh(part, index);
        UpdateEditor();
    }

    void MarkDirty()
    {
        if (!_dirty) { _dirty = true; Changed(); }
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Unsaved changes.";
    }

    void UpdateButtons()
    {
        _save.Enabled = _dirty && PlayerStore.CanSave(World);
        _discard.Enabled = _dirty;
        _nbt.Enabled = _player != null;
        _moveTo.Enabled = _player != null;
        if (!PlayerStore.CanSave(World) && _player != null)
        {
            _status.ForeColor = C.FG_FAINT;
            _status.Text = World.OpenedForRescue ? "A world opened for rescue is only read."
                : World is Core.Bedrock.ThreeDsWorld ? "A 3DS world is only read (the game deletes a 3DS world that was changed): its things can be copied into another world."
                : "Players of this kind of world can be looked at but not saved.";
        }
    }

    public override async Task SaveAsync()
    {
        if (!_dirty || _inv == null || _player == null) return;
        if (_selected != null) Edited();
        if (WorldUse.InUse(World) is string busy) { _status.ForeColor = C.ORANGE; _status.Text = busy; return; }
        var inv = _inv;
        var player = _player;
        bool ok = await Main.RunAsync($"Saving {player.Name}'s inventory", progress =>
        {
            Backup.WorldBeforeSave(World, progress);
            inv.WriteBack();
            PlayerStore.Save(World, player);
        });
        if (!ok) return;
        _dirty = false;
        Changed();
        FillPlayers();
        _status.ForeColor = C.GREEN;
        _status.Text = "Saved. A backup was made first.";
    }

    /// <summary>One inventory slot: the item's picture (or name), its count, enchantment glint and damage.</summary>
    sealed class SlotView : Button
    {
        readonly ToolTip _tip = new();
        readonly Font _countFont = Theme.Ui(9f, FontStyle.Bold);
        public InventoryItem? Item { get; set; }
        public Bitmap? Icon { get; set; }
        public string Label { get; set; } = "";
        public string SlotName { get; set; } = "";
        public bool Selected { get; set; }
        public string Tip { set => _tip.SetToolTip(this, value); }

        public SlotView()
        {
            // A real button (keyboard and screen readers work), drawn entirely here.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(7.5f);
            UseMnemonic = false;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var bg = new SolidBrush(Selected ? C.BG4 : C.BG3)) g.FillRectangle(bg, r);
            var border = Selected ? C.ACCENT2 : Focused ? C.FG_DIM : C.BORDER;
            using (var pen = new Pen(border, Selected ? this.Px(2) : 1) { Alignment = PenAlignment.Inset }) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            if (Item == null) return;
            int pad = this.Px(7);
            var area = new Rectangle(pad, pad, r.Width - 2 * pad, r.Height - 2 * pad);
            if (Icon != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Icon, area);
            }
            else TextRenderer.DrawText(g, Label, Font, area, C.FG, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Item.Enchantments.Count > 0)
                using (var glint = new Pen(Color.FromArgb(200, 170, 90, 255), this.Px(2)) { Alignment = PenAlignment.Inset }) g.DrawRectangle(glint, 2, 2, r.Width - 5, r.Height - 5);
            if (Item.Damage > 0)
                using (var dmg = new SolidBrush(C.ORANGE)) g.FillRectangle(dmg, pad, r.Height - this.Px(5), r.Width - 2 * pad, this.Px(2));
            if (Item.Count != 1)
            {
                var size = TextRenderer.MeasureText(g, Item.Count.ToString(), _countFont);
                var at = new Point(r.Width - size.Width - this.Px(2), r.Height - size.Height - this.Px(1));
                TextRenderer.DrawText(g, Item.Count.ToString(), _countFont, new Point(at.X + 1, at.Y + 1), Color.Black);
                TextRenderer.DrawText(g, Item.Count.ToString(), _countFont, at, Color.White);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tip.Dispose(); _countFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
