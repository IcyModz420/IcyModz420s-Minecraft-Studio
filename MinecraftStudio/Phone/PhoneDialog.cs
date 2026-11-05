// MinecraftStudio/Phone/PhoneDialog.cs: Gets Minecraft worlds off an Android phone without root: over a USB
// cable in "File transfer" mode, or through Android's debugging (USB or wireless).
// Owns: PhoneDialog.
using MinecraftStudio.Core.IO;
using MinecraftStudio.UI;

namespace MinecraftStudio.Phone;

/// <summary>
/// Gets Minecraft worlds off an Android phone without root: over a USB cable in "File transfer" mode, or through
/// Android's debugging (USB or wireless). Each world is copied into a folder on this PC and checked file by file.
/// Worlds Android keeps inside the app itself cannot be read from outside; the dialog explains how to get those out.
/// </summary>
sealed class PhoneDialog : DarkDialog
{
    readonly MainForm _main;
    readonly DarkComboBox _phones;
    readonly ListView _worlds;
    readonly DarkTextField _destination, _pairAddress, _pairCode, _connectAddress;
    readonly Label _status, _adbStatus, _notes;
    readonly Button _look, _find, _copy, _cancel, _getTools, _pair, _connect, _fromApp;
    readonly List<PhoneSource> _sources = [];
    CancellationTokenSource? _work;
    string? _adb;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "From Android");

    public PhoneDialog(MainForm main) : base("Get worlds from an Android phone", 820)
    {
        _main = main;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(this.Px(640), this.Px(560));
        ClientSize = new Size(this.Px(820), this.Px(700));

        var intro = new Label
        {
            Text = "Plug the phone in with a USB cable, unlock it, and choose \"File transfer\" in the USB notification. Then look for phones. " +
                   "(Phones that show nothing that way can use Android's debugging: USB debugging, or Wireless debugging with no cable, below.)",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };

        // Phones.
        var phoneRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        _look = Theme.Button("Look for phones", true);
        _phones = new DarkComboBox { Width = this.Px(360), AccessibleName = "Phone", Margin = new Padding(this.Px(4), this.Px(7), this.Px(4), 0) };
        _find = Theme.Button("Find worlds");
        _look.Click += async (_, _) => await LookForPhones();
        _find.Click += async (_, _) => await FindWorlds();
        phoneRow.Controls.AddRange([_look, _phones, _find]);

        // Debugging (adb).
        var adbBox = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        _adbStatus = new Label { AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false };
        _getTools = Theme.Button("Get Google's platform tools (adb)…");
        _getTools.Click += async (_, _) => await GetTools();
        var wireless = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _pairAddress = new DarkTextField { Width = this.Px(170), Height = this.Px(28), AccessibleName = "Pairing address" };
        _pairAddress.Box.PlaceholderText = "IP:port for pairing";
        _pairCode = new DarkTextField { Width = this.Px(100), Height = this.Px(28), AccessibleName = "Pairing code" };
        _pairCode.Box.PlaceholderText = "6-digit code";
        _pair = Theme.Button("Pair");
        _connectAddress = new DarkTextField { Width = this.Px(170), Height = this.Px(28), AccessibleName = "Connect address" };
        _connectAddress.Box.PlaceholderText = "IP:port to connect";
        _connect = Theme.Button("Connect");
        _pair.Click += async (_, _) => await Wireless(pair: true);
        _connect.Click += async (_, _) => await Wireless(pair: false);
        foreach (var c in new Control[] { _pairAddress, _pairCode, _connectAddress }) c.Margin = new Padding(this.Px(4), this.Px(7), this.Px(4), 0);
        wireless.Controls.AddRange([_pairAddress, _pairCode, _pair, _connectAddress, _connect]);
        _fromApp = Theme.Button("Get worlds kept inside the app (no root)…");
        _fromApp.Click += async (_, _) => await FromApp();
        adbBox.Controls.AddRange([_adbStatus, _getTools, wireless, _fromApp]);

        // Worlds.
        _worlds = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, BackColor = C.BG, ForeColor = C.FG,
            BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "Worlds on the phone",
        };
        _worlds.Columns.Add("World", this.Px(240));
        _worlds.Columns.Add("Size", this.Px(90), HorizontalAlignment.Right);
        _worlds.Columns.Add("Last played", this.Px(130));
        _worlds.Columns.Add("On the phone", this.Px(300));
        Theme.DarkList(_worlds);
        Theme.FillLastColumn(_worlds);

        // Destination and copy.
        var destRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        _destination = new DarkTextField(DefaultFolder) { Width = this.Px(520), Height = this.Px(28), AccessibleName = "Copy to folder", Margin = new Padding(0, this.Px(7), this.Px(4), 0) };
        var browse = Theme.Button("Browse…");
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Copy the worlds into", UseDescriptionForTitle = true, SelectedPath = _destination.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _destination.Text = dlg.SelectedPath;
        };
        _copy = Theme.Button("Copy selected worlds", true);
        _copy.Click += async (_, _) => await CopySelected();
        destRow.Controls.AddRange([new Label { Text = "Copy to", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(0, this.Px(12), this.Px(6), 0) }, _destination, browse, _copy]);

        _notes = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0),
            Text = HelpText,
        };
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _cancel = Theme.Button("Stop");
        _cancel.Enabled = false;
        _cancel.Click += (_, _) => _work?.Cancel();
        buttons.Controls.Add(_cancel);

        Controls.Add(_worlds);
        Controls.Add(_status);
        Controls.Add(destRow);
        Controls.Add(_notes);
        Controls.Add(adbBox);
        Controls.Add(phoneRow);
        Controls.Add(intro);
        Shown += async (_, _) => await LookForPhones();
        FormClosing += (_, e) =>
        {
            if (_work != null) { e.Cancel = true; _status.Text = "Stop the copy first (it is checking every file)."; }
        };
    }

    const string HelpText =
        "Worlds not showing up? Minecraft on Android keeps worlds either in the app itself (Settings, Storage, File Storage Location: Application) " +
        "or where a computer can reach them (External). To get worlds out of the app's own storage, with debugging on (below):\n" +
        "• No root, Android 11 or older: \"Get worlds kept inside the app (no root)\" uses Android's own backup — tap \"Back up my data\" on the phone and leave the password blank.\n" +
        "• Rooted phone (any Android): connect with debugging and the app's own storage is searched too (allow \"Shell\" when the root app asks).\n" +
        "• Android 12 or newer without root: Android no longer lets a computer back up the app, so a rooted phone is the only way. Otherwise, in Minecraft tap the pencil next to the world, choose Export World, save the .mcworld in Downloads, and it shows up here.";

    // ------------------------------------------------------------------ work

    async Task Busy(string what, Func<CancellationToken, IProgress<string>, Task> work)
    {
        _work = new CancellationTokenSource();
        foreach (var b in new[] { _look, _find, _copy, _getTools, _pair, _connect, _fromApp }) b.Enabled = false;
        _cancel.Enabled = true;
        _status.ForeColor = C.FG;
        _status.Text = what + "…";
        var progress = new Progress<string>(s => _status.Text = s);
        try { await work(_work.Token, progress); }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or HttpRequestException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = ex.Message;
        }
        finally
        {
            _work.Dispose();
            _work = null;
            foreach (var b in new[] { _look, _find, _copy, _getTools, _pair, _connect, _fromApp }) b.Enabled = true;
            _cancel.Enabled = false;
            UpdateAdb();
        }
    }

    void UpdateAdb()
    {
        _adb = AdbPhone.FindAdb();
        _adbStatus.Text = _adb != null
            ? $"Android debugging: ready ({_adb}). For no cable: on the phone, Developer options, Wireless debugging, \"Pair device with pairing code\"; type the address and code shown there, Pair, then Connect with the address on the Wireless debugging screen."
            : "Android debugging is optional: it needs Google's free platform tools (adb), about 7 MB from dl.google.com. Get them only if USB file transfer does not show your worlds.";
        _getTools.Visible = _adb == null;
        _pair.Enabled = _connect.Enabled = _adb != null && _work == null;
        _fromApp.Visible = _adb != null;
        _fromApp.Enabled = _adb != null && _work == null;
    }

    async Task LookForPhones() => await Busy("Looking for phones", async (ct, progress) =>
    {
        var notes = new List<string>();
        var found = await Task.Run(() =>
        {
            var list = new List<PhoneSource>();
            try { list.AddRange(MtpPhone.Find()); }
            catch (IOException e) { notes.Add("USB file transfer: " + e.Message); }
            if (AdbPhone.FindAdb() is { } adb)
            {
                try { list.AddRange(AdbPhone.Find(adb, notes)); }
                catch (IOException e) { notes.Add("Android debugging: " + e.Message); }
            }
            return list;
        }, ct);
        _sources.Clear();
        _sources.AddRange(found);
        _phones.Items.Clear();
        foreach (var s in found) _phones.Items.Add(new UI.ComboItem(s.Label, null, s));
        if (found.Count > 0) _phones.SelectedIndex = 0;
        _status.Text = found.Count == 0
            ? "No phone found. Unlock the phone, plug it in, and choose File transfer in its USB notification (or connect it with debugging below), then look again."
            : $"Found {found.Count} way(s) to reach a phone. Choose one and press Find worlds.";
        if (notes.Count > 0) _status.Text += "\n" + string.Join("\n", notes);
        if (found.Count == 1) await FindWorldsCore(ct, progress);
    });

    async Task FindWorlds() => await Busy("Finding worlds", FindWorldsCore);

    async Task FindWorldsCore(CancellationToken ct, IProgress<string> progress)
    {
        if (_phones.SelectedItem is not UI.ComboItem { Tag: PhoneSource source }) { _status.Text = "Choose a phone first."; return; }
        var notes = new List<string>();
        var worlds = await Task.Run(() => source.FindWorlds(notes, progress, ct), ct);
        _worlds.BeginUpdate();
        _worlds.Items.Clear();
        foreach (var w in worlds.OrderBy(w => w.IsPackage).ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ListViewItem(w.IsPackage ? w.Name + "  (exported .mcworld)" : w.Name) { Tag = w, Checked = true };
            item.SubItems.Add(BinaryUtil.FormatSize(w.Size));
            item.SubItems.Add(w.Modified?.ToString("g") ?? "");
            item.SubItems.Add(w.Where);
            _worlds.Items.Add(item);
        }
        _worlds.EndUpdate();
        _status.Text = worlds.Count == 0
            ? "No worlds were found where a computer can reach them. See the note below on worlds kept inside the app."
            : $"{worlds.Count} world(s) found. Tick the ones to copy.";
        var meaningful = notes.Where(n => !n.StartsWith("Looked in")).ToList();
        if (meaningful.Count > 0) _status.Text += "\n" + string.Join("\n", meaningful);
    }

    async Task CopySelected()
    {
        var picked = _worlds.CheckedItems.Cast<ListViewItem>().Select(i => (PhoneWorld)i.Tag!).ToList();
        if (picked.Count == 0) { _status.Text = "Tick the worlds to copy first."; return; }
        if (_phones.SelectedItem is not UI.ComboItem { Tag: PhoneSource source }) return;
        string folder = _destination.Text.Trim();
        if (folder.Length == 0) { _status.Text = "Choose a folder to copy into."; return; }
        var done = new List<string>();
        var failed = new List<string>();
        await Busy("Copying", async (ct, progress) =>
        {
            Directory.CreateDirectory(folder);
            foreach (var w in picked)
            {
                ct.ThrowIfCancellationRequested();
                string dest = UniquePath(folder, w);
                try
                {
                    await Task.Run(() => source.Copy(w, dest, progress, ct), ct);
                    done.Add(dest);
                }
                catch (IOException e) { failed.Add($"{w.Name}: {e.Message}"); }
            }
        });
        string report = (done.Count > 0 ? $"Copied and checked (every file arrived whole):\n{string.Join("\n", done)}" : "") +
                        (failed.Count > 0 ? (done.Count > 0 ? "\n\n" : "") + $"Not copied:\n{string.Join("\n", failed)}" : "");
        if (report.Length == 0) return;
        _status.Text = done.Count > 0 ? $"Copied {done.Count} world(s) into {folder}." : "Nothing was copied.";
        if (done.Count > 0 && Dialogs.Ask(this, report + "\n\nOpen " + (done.Count == 1 ? "the world" : "the first world") + " in " + Dialogs.AppName + " now?") == DialogResult.Yes)
        {
            Close();
            await _main.OpenPath(done[0]);
        }
        else if (done.Count == 0) Dialogs.Error(this, report);
    }

    /// <summary>A new folder (or .mcworld file) named after the world, never on top of an existing one.</summary>
    static string UniquePath(string folder, PhoneWorld w)
    {
        string name = SafeName.For(w.Name, "World");
        string ext = w.IsPackage ? ".mcworld" : "";
        string path = Path.Combine(folder, name + ext);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n}){ext}");
        return path;
    }

    async Task GetTools()
    {
        if (Dialogs.Ask(this, $"Download Google's Android platform tools (about 7 MB) from\n{PlatformTools.Url}\n\nThey are unpacked into\n{AdbPhone.ToolsFolder}\nand used only if adb.exe is signed by Google.") != DialogResult.Yes) return;
        await Busy("Getting the platform tools", async (ct, progress) =>
        {
            string adb = await PlatformTools.Download(progress, ct);
            _status.Text = "Ready: " + adb + ". Turn on USB debugging (or Wireless debugging) on the phone, then look for phones.";
        });
    }

    /// <summary>Gets worlds out of the app's own storage without root, through Android's backup (Android 11 and older).</summary>
    async Task FromApp()
    {
        if (_phones.SelectedItem is not UI.ComboItem { Tag: AdbPhone adb })
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "Choose a phone connected with debugging first (USB debugging, or Wireless debugging). This way uses Android's backup, which needs debugging.";
            return;
        }
        string folder = _destination.Text.Trim();
        if (folder.Length == 0) { _status.Text = "Choose a folder to copy into first."; return; }
        if (Dialogs.Ask(this,
            "Get worlds out of Minecraft's own storage without root, using Android's backup?\n\n" +
            "When this starts, the phone shows a \"Full backup\" screen: tap \"Back up my data\" and leave the password box blank.\n\n" +
            "This works on Android 11 and older. On Android 12 and newer, Android no longer lets a computer back up an ordinary app this way, so nothing will come back — a rooted phone is then the only way.") != DialogResult.Yes) return;
        var notes = new List<string>();
        var copied = new List<string>();
        await Busy("Getting worlds from the app", async (ct, progress) =>
        {
            copied = await Task.Run(() => adb.BackupWorlds(folder, progress, ct, notes), ct);
        });
        if (copied.Count > 0)
        {
            _status.Text = $"Got {copied.Count} world(s) from the app into {folder}.";
            if (Dialogs.Ask(this, "Got:\n" + string.Join("\n", copied) + "\n\nOpen " + (copied.Count == 1 ? "it" : "the first one") + " in " + Dialogs.AppName + " now?") == DialogResult.Yes)
            {
                Close();
                await _main.OpenPath(copied[0]);
            }
        }
        else if (notes.Count > 0)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = string.Join("\n", notes);
        }
    }

    async Task Wireless(bool pair)
    {
        if (_adb == null) return;
        string address = (pair ? _pairAddress.Text : _connectAddress.Text).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(address, @"^[\w.\-\[\]:]+:\d{1,5}$")) { _status.Text = "Type the address as shown on the phone, such as 192.168.1.20:37099."; return; }
        string code = _pairCode.Text.Trim();
        if (pair && !System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{6}$")) { _status.Text = "Type the 6-digit pairing code shown on the phone."; return; }
        await Busy(pair ? "Pairing" : "Connecting", async (ct, _) =>
        {
            string adb = _adb!;
            string answer = await Task.Run(() => pair ? AdbPhone.Pair(adb, address, code) : AdbPhone.Connect(adb, address), ct);
            _status.Text = answer.Length > 0 ? answer : "Done.";
        });
        if (!pair) await LookForPhones();
    }
}
