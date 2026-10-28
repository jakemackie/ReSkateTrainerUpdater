using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui.Controls;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>
/// The one window, built like the ReSkate launcher: no title bar (just minimise and close buttons in the corner), a
/// chip and a big wordmark at the top left, a STATUS card on the left, and on the right the folder, the optional zip,
/// and one big tile that does the work. The tile either downloads the newest Trainer from GitHub or
/// installs a zip the user dropped. Both routes end in the same InstallAsync.
/// This class only handles the window. What to show, what to download and how to install lives in Core.
/// </summary>
internal sealed class UpdaterForm : Form, IBackdropHost
{
    private readonly UpdaterSettings _settings = UpdaterSettings.Load();

    private readonly HeaderBar _header = new();
    private readonly StatusCard _statusCard = new();
    private readonly CardPanel _logCard = new() { Title = "ACTIVITY LOG", Visible = false };
    private readonly TextBox _log = new();
    private readonly InfoTile _folderTile = new() { Glyph = TileGlyph.Folder, PathSubtitle = true };
    private readonly InfoTile _zipTile = new() { Glyph = TileGlyph.Zip };
    private readonly ActionTileControl _actionTile = new();
    private readonly OptionRow _backupRow = new()
    {
        Title = "BACK UP REPLACED FILES",
        Description = "Keeps a copy of your old files, just in case.",
    };
    private readonly SquareButton _logButton = new() { Text = "ACTIVITY LOG", Dark = true };
    private readonly SquareButton _copyLogButton = new() { Text = "COPY LOG", Dark = true, Visible = false };
    private readonly SquareButton _reportButton = new() { Text = "REPORT A PROBLEM", Dark = true, Visible = false };
    private readonly SquareButton[] _linkButtons = Enumerable.Range(0, HomeView.MaxLinks)
        .Select(_ => new SquareButton { Compact = true, Visible = false }).ToArray();
    private readonly WindowButtons _windowButtons = new();
    private readonly string _version = typeof(UpdaterForm).Assembly.GetName().Version?.ToString(3) ?? "";

    // The zip, whether the user chose it or it was downloaded from GitHub.
    private string? _zipPath;
    private string? _zipProblem;
    private TrainerPackage? _package;
    private bool _checkingZip;
    private bool _zipFromOnline;
    private int _zipGeneration;

    // The ReSkate folder.
    private string? _folder;
    private InstallFolderInfo? _folderInfo;
    private InstallFolderInfo? _lastFolderInfo;
    private bool _checkingFolder;
    private int _folderGeneration;

    // Online updates. One HTTP client for the app's lifetime.
    private readonly HttpClient _http = TrainerDownloader.CreateClient();
    private TrainerManifest? _manifest;
    private OnlineStatus _online = OnlineStatus.Checking;
    private OnlineOffer _offer = OnlineOffer.Nothing;
    private AppOffer? _appOffer;
    private bool _checkingOnline;

    // A ReSkate folder found on this PC while the user hasn't chosen one yet.
    private string? _suggestedFolder;

    // Programs found using the game's files when an install was blocked, and what to run again once they're closed.
    private enum Retry { Install, Restore }
    private Retry _retry;

    // What the big tile does right now, kept from the last time the screen was drawn.
    private TileAction _tileAction = TileAction.None;

    // The outcome of the last install, shown until the user changes something.
    private Advice? _result;

    // While something runs, this replaces the normal screen.
    private HomeView? _busyView;
    private bool _installing;

    public UpdaterForm(bool justUpdated = false)
    {
        Text = AppInfo.Name;
        // Everything is placed by hand from the launcher's measurements and scaled by the display's DPI (see LayoutAll).
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        // No title bar, like the launcher. The window has a fixed size, so there is no maximise button either.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;

        // The launcher's default size, but never bigger than the screen's usable area (a small laptop screen still fits it).
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        float k = DpiScale;
        ClientSize = new Size(
            Math.Min((int)(DesignWidth * k), area.Width - (int)(40 * k)),
            Math.Min((int)(DesignHeight * k), area.Height - (int)(40 * k)));
        StartPosition = FormStartPosition.CenterScreen;
        RestoreWindowPosition();

        if (AppIcon.Load() is { } icon) Icon = icon;

        BuildLayout();
        MakeDraggable(this);
        _header.SetCreator(UpdateAdvisor.DescribeCreator());

        // Restore what the user had last time.
        _backupRow.SetCheckedImmediate(_settings.BackupReplacedFiles);
        if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
            _folder = _settings.LastFolder;

        _folderTile.Click += (_, _) => { if (!_installing) BrowseForFolder(); };
        _zipTile.Click += (_, _) => { if (!_installing) BrowseForZip(); };
        _zipTile.ClearClick += (_, _) => { if (!_installing) ClearZipByUser(); };
        _actionTile.Click += async (_, _) => await OnTileClickAsync();
        _logButton.Click += (_, _) => ToggleLog();
        _copyLogButton.Click += (_, _) => CopyLog();
        _reportButton.Click += (_, _) => ReportProblem();
        _header.StatusClick += (_, _) => { _ = RefreshOnlineAsync(); };
        foreach (var button in _linkButtons) button.Click += async (_, _) => await OnLinkClickAsync(button);
        _windowButtons.MinimizeClick += (_, _) => WindowState = FormWindowState.Minimized;
        _windowButtons.CloseClick += (_, _) => Close();
        _backupRow.CheckedChanged += (_, _) =>
        {
            _settings.BackupReplacedFiles = _backupRow.Checked;
            _settings.Save();
        };

        // The previous version started this one after swapping itself out, and says so here.
        if (justUpdated) _result = UpdateAdvisor.DescribeAppUpdated(_version);

        EnableDropOn(this);
        Render();
    }

    // A window with no border still needs these styles so that clicking its taskbar button minimises and restores it.
    protected override CreateParams CreateParams
    {
        get
        {
            const int WsMinimizeBox = 0x00020000;
            const int WsSysMenu = 0x00080000;
            var cp = base.CreateParams;
            cp.Style |= WsMinimizeBox | WsSysMenu;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyWindowFrame(Handle, Theme.WindowBorder);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = Task.Run(TrainerPackageLoader.CleanUpStaleTempFolders);

        // After an app update, the previous version's exe is left beside this one. Remove it.
        if (Environment.ProcessPath is { } exePath && AppUpdater.CanUpdateInPlace(exePath))
            _ = Task.Run(() => AppUpdater.CleanUpLeftovers(exePath));

        // If the setup installed this copy, keep its Add/Remove Programs entry showing the version that is really running.
        _ = Task.Run(() => InstallRegistration.SyncVersion(_version));

        // One quiet request for latest.json. Any failure just leaves the screen as it would be offline.
        _ = CheckOnlineAsync();

        // Show which version is already installed in the folder remembered from last time, or look for one.
        if (_folder is not null) _ = InspectFolderAsync(_folder);
        else _ = FindFolderAsync();
    }

    // Looks in the usual places for a ReSkate folder, so a first-time user is offered one instead of an empty picker.
    private async Task FindFolderAsync()
    {
        string? found = await Task.Run(() => ReSkateLocator.Find(ReSkateLocator.DefaultRoots()));
        if (IsDisposed || _folder is not null || found is null) return;

        _suggestedFolder = found;
        Log("Found what looks like a ReSkate folder: " + found);
        Render();
    }

    // Opens where it was last closed, as long as that is still on a screen.
    private void RestoreWindowPosition()
    {
        if (_settings is not { WindowLeft: { } left, WindowTop: { } top }) return;

        var wanted = new Rectangle(left, top, Width, Height);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.Contains(wanted.Left + 60, wanted.Top + 20))) return;

        StartPosition = FormStartPosition.Manual;
        Location = new Point(left, top);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposePackage();
        _http.Dispose();
        base.OnFormClosed(e);
    }

    // ---------- Layout ----------

    // The window is laid out by hand, from the launcher's own measurements (taken at its default size of 1440 by 840):
    // 60 pixels in from the left and 64 from the right, 56 up from the bottom, and 16 between the tiles.
    // Cards are placed directly on the form so that they can draw over its backdrop and let it show faintly through.
    private const float DesignWidth = 1440f;
    private const float DesignHeight = 840f;
    private const float MarginLeft = 60f;
    private const float MarginRight = 64f;
    private const float MarginBottom = 56f;
    private const float Gap = 16f;

    private HomeView? _homeView;
    private Bitmap? _backdrop;

    private float DpiScale => DeviceDpi / 96f;

    private void BuildLayout()
    {
        BuildLogCard();

        foreach (Control control in new Control[]
                 {
                     _header, _statusCard, _logCard, _backupRow, _logButton, _copyLogButton, _reportButton,
                     _folderTile, _zipTile, _actionTile, _windowButtons,
                 }.Concat(_linkButtons))
        {
            Controls.Add(control);
        }
        // The card's own buttons sit on top of the card.
        foreach (var button in _linkButtons) button.BringToFront();
        _windowButtons.BringToFront();
        LayoutAll();
    }

    // Puts every control where it belongs for the current window size. Cheap, so it runs whenever the card heights may change.
    private void LayoutAll()
    {
        float k = DpiScale;
        float w = ClientSize.Width / k;
        float h = ClientSize.Height / k;
        int X(float v) => (int)Math.Round(v * k);
        Rectangle R(float x, float y, float width, float height) => new(X(x), X(y), X(width), X(height));

        // Top left: the two cards and the wordmark. Top right: minimise and close, flush in the corner.
        _header.Bounds = R(50, 40, 720, 140);
        _windowButtons.Bounds = new Rectangle(ClientSize.Width - X(WindowButtons.ButtonWidth * 2), 0, X(WindowButtons.ButtonWidth * 2), X(WindowButtons.ButtonHeight));

        // Right column, stacked up from the bottom like the launcher's SETTINGS, MOD MANAGER and PLAY.
        float columnWidth = Math.Max(330f, w * 0.25f);
        float columnX = w - MarginRight - columnWidth;
        float tileHeight = 96f;
        float actionHeight = Math.Clamp(h * 0.19f, 136f, 172f);
        float y = h - MarginBottom - actionHeight;
        _actionTile.Bounds = R(columnX, y, columnWidth, actionHeight);
        y -= Gap + tileHeight;
        _zipTile.Bounds = R(columnX, y, columnWidth, tileHeight);
        y -= Gap + tileHeight;
        _folderTile.Bounds = R(columnX, y, columnWidth, tileHeight);

        // Left column: the status card, as tall as its content, with the backup option under it.
        float leftWidth = Math.Min(500f, w * 0.345f);
        float cardTop = 200f;
        int cardHeight = _homeView is null ? X(160) : _statusCard.PreferredHeight(_homeView, X(leftWidth));
        _statusCard.Bounds = new Rectangle(X(MarginLeft), X(cardTop), X(leftWidth), cardHeight);
        _backupRow.Bounds = new Rectangle(X(MarginLeft), _statusCard.Bottom + X(Gap), X(leftWidth), X(72));

        // Bottom left: the log button, like the launcher's CHANGELOGS. The log itself opens over the status card.
        // While the log is open, two more buttons sit beside it: one copies the log, the other reports a problem.
        float buttonHeight = 52f;
        _logButton.Bounds = R(MarginLeft, h - MarginBottom - buttonHeight, 190, buttonHeight);
        _copyLogButton.Bounds = R(MarginLeft + 190 + Gap, h - MarginBottom - buttonHeight, 160, buttonHeight);
        _reportButton.Bounds = R(MarginLeft + 190 + Gap + 160 + Gap, h - MarginBottom - buttonHeight, 250, buttonHeight);
        _logCard.Bounds = R(MarginLeft, cardTop, columnX - MarginLeft - 32, h - MarginBottom - buttonHeight - Gap - cardTop);

        LayoutLinkButtons(X);
    }

    // The status card's small buttons, in a row along the bottom of the card. Any that don't fit are left out.
    private void LayoutLinkButtons(Func<float, int> x)
    {
        var links = _homeView?.Links ?? Array.Empty<CardLink>();
        int left = _statusCard.Left + x(22);
        int right = _statusCard.Right - x(20);
        int buttonHeight = x(34);
        int top = _statusCard.Bottom - x(StatusCard.LinkRowHeight) + x(8);

        for (int i = 0; i < _linkButtons.Length; i++)
        {
            var button = _linkButtons[i];
            bool shown = false;
            if (i < links.Count && _statusCard.Visible)
            {
                button.Text = links[i].Text;
                button.Tag = links[i];
                int width = button.PreferredWidth();
                if (left + width <= right)
                {
                    button.Bounds = new Rectangle(left, top, width, buttonHeight);
                    left += width + x(Gap / 2);
                    shown = true;
                }
            }
            button.Visible = shown;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        RebuildBackdrop();
        LayoutAll();
    }

    private void RebuildBackdrop()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (_backdrop is not null && _backdrop.Size == ClientSize) return;
        _backdrop?.Dispose();
        _backdrop = Backdrop.Create(ClientSize.Width, ClientSize.Height, DpiScale);
    }

    // The window's own background is the backdrop, and so is whatever shows through a see-through card.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RebuildBackdrop();
        if (_backdrop is not null) e.Graphics.DrawImageUnscaled(_backdrop, 0, 0);
        else e.Graphics.Clear(Theme.Bg);
    }

    void IBackdropHost.DrawBackdrop(Graphics g, Rectangle childBounds)
    {
        RebuildBackdrop();
        if (_backdrop is null)
        {
            g.Clear(Theme.Bg);
            return;
        }

        // An exact copy of the part of the backdrop under the card, with no resampling.
        var state = g.Save();
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half; // with None, the first row and column are only half covered

        // Clamp the edges, so the copy never reaches past the part of the backdrop it was asked for.
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        g.DrawImage(_backdrop, new Rectangle(0, 0, childBounds.Width, childBounds.Height),
            childBounds.X, childBounds.Y, childBounds.Width, childBounds.Height, GraphicsUnit.Pixel, attributes);
        g.Restore(state);
    }

    private void BuildLogCard()
    {
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.CardSolid;
        _log.ForeColor = Theme.Muted;
        _log.Font = Theme.Mono;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.TabStop = false;
        _log.HandleCreated += (_, _) => NativeMethods.UseDarkScrollbars(_log.Handle);

        _logCard.Padding = new Padding(22, 60, 14, 18);
        _logCard.Controls.Add(_log);
    }

    private void ToggleLog()
    {
        bool show = !_logCard.Visible;
        _logCard.Visible = show;
        _statusCard.Visible = !show;
        _backupRow.Visible = !show;
        _copyLogButton.Visible = show;
        _reportButton.Visible = show;
        _copyLogButton.Text = "COPY LOG";
        _logButton.Text = show ? "BACK TO STATUS" : "ACTIVITY LOG";
        LayoutAll(); // the card's own buttons only show with the card
    }

    // The log as text, with the app and Windows version on top so a pasted log says where it came from.
    private void CopyLog()
    {
        string text = UpdateAdvisor.DescribeLogHeader(_version, RuntimeInformation.OSDescription) + "\r\n" + _log.Text;
        try
        {
            Clipboard.SetText(text);
            _copyLogButton.Text = "COPIED";
        }
        catch (ExternalException)
        {
            // Another program has the clipboard open. Nothing to do but let them press it again.
            _copyLogButton.Text = "TRY AGAIN";
        }
    }

    // Without a title bar, the window is moved by dragging any empty part of it, like the launcher.
    private void MakeDraggable(Control control)
    {
        if (control is Form or TableLayoutPanel or Panel or HeaderBar or StatusCard)
        {
            control.MouseDown += (_, e) =>
            {
                // The GitHub chip in the header is a button, so it keeps its clicks.
                if (control is HeaderBar header && header.IsOverStatus(e.Location)) return;
                if (e.Button == MouseButtons.Left) NativeMethods.DragWindow(Handle);
            };
        }
        foreach (Control child in control.Controls) MakeDraggable(child);
    }

    // ---------- Drag and drop ----------

    // Every control has to accept drops, otherwise the cursor shows "not allowed" while over it.
    private void EnableDropOn(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += OnDragOver;
        control.DragOver += OnDragOver;
        control.DragLeave += OnDragLeave;
        control.DragDrop += OnDragDrop;
        foreach (Control child in control.Controls) EnableDropOn(child);
    }

    private static bool TryGetDroppedZip(IDataObject? data, out string zipPath)
    {
        zipPath = "";
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return false;

        var found = files.FirstOrDefault(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (found is null) return false;

        zipPath = found;
        return true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!_installing && TryGetDroppedZip(e.Data, out _))
        {
            e.Effect = DragDropEffects.Copy;
            _zipTile.SetHighlight(true);
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, EventArgs e) => _zipTile.SetHighlight(false);

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        _zipTile.SetHighlight(false);
        if (_installing) return;

        if (TryGetDroppedZip(e.Data, out var zipPath))
        {
            _ = LoadZipAsync(zipPath);
        }
        else
        {
            _result = new Advice("That doesn't look like a .zip file. Drop the ReSkate Trainer zip you downloaded.", Severity.Warning);
            Render();
        }
    }

    // ---------- Choosing the zip ----------

    private void BrowseForZip()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the ReSkate Trainer zip",
            Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _ = LoadZipAsync(dialog.FileName);
    }

    private void DisposePackage()
    {
        _package?.Dispose();
        _package = null;
    }

    // Forgets the zip, whether chosen or downloaded, so the screen goes back to the online offer.
    private void ClearZip()
    {
        _zipGeneration++;
        DisposePackage();
        _zipPath = null;
        _zipProblem = null;
        _checkingZip = false;
        _zipFromOnline = false;
    }

    private void ClearZipByUser()
    {
        ClearZip();
        _result = null;
        Render();
    }

    private async Task LoadZipAsync(string path)
    {
        int generation = ++_zipGeneration;
        _zipPath = path;
        _zipProblem = null;
        _zipFromOnline = false;
        _result = null;
        DisposePackage();

        _checkingZip = true;
        Render();

        TrainerPackage? package = null;
        string? problem = null;
        try
        {
            package = await Task.Run(() => TrainerPackageLoader.Load(path));
        }
        catch (InvalidDataException)
        {
            problem = "That isn't a valid zip file. It may be damaged or incomplete, so try downloading it again.";
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the window must leave its "checking" state and tell the user.
            problem = "Couldn't open that zip: " + ex.Message;
        }

        // The user picked something else while this one was being checked: throw this result away.
        if (generation != _zipGeneration)
        {
            package?.Dispose();
            return;
        }

        _checkingZip = false;
        _package = package;
        _zipProblem = problem;
        Render();
    }

    // ---------- Choosing the ReSkate folder ----------

    private void BrowseForFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose your ReSkate folder (where ReSkateLauncher.exe lives)",
            UseDescriptionForTitle = true,
            SelectedPath = _folder ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ChooseFolder(dialog.SelectedPath);
    }

    private void ChooseFolder(string path)
    {
        _folder = path;
        _lastFolderInfo = null;
        _suggestedFolder = null;
        _result = null;
        _settings.LastFolder = _folder;
        _settings.Save();
        _ = InspectFolderAsync(_folder);
    }

    private async Task InspectFolderAsync(string path)
    {
        int generation = ++_folderGeneration;
        _checkingFolder = true;
        _folderInfo = null;
        Render();

        InstallFolderInfo? info = null;
        try
        {
            info = await Task.Run(() => InstallFolderInspector.Inspect(path));
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the window must leave its "checking" state.
            Log("Couldn't read the folder: " + ex.Message);
        }

        if (generation != _folderGeneration) return;

        _checkingFolder = false;
        _folderInfo = info;
        if (info is not null) _lastFolderInfo = info;
        _offer = UpdateAdvisor.DescribeOnlineOffer(_manifest, _folderInfo);
        Render();
    }

    // ---------- Drawing the screen ----------

    private UpdateContext CurrentContext() =>
        new(_checkingZip, _zipPath, _zipProblem, _package, _folder, _checkingFolder, _folderInfo, _offer, _online, _appOffer, _suggestedFolder);

    // Draws everything from the current state. The decisions are made in UpdateAdvisor.
    private void Render()
    {
        HomeView view = _busyView ?? UpdateAdvisor.DescribeHome(CurrentContext());
        // While the folder is being looked at again after an install, what was known before still says whether PLAY can start it.
        if (_busyView is null && _result is { } result) view = UpdateAdvisor.WithResult(view, result, _folderInfo ?? _lastFolderInfo);

        _homeView = view;
        _statusCard.Show(view);
        _actionTile.Show(view.Tile);
        LayoutAll(); // the status card is as tall as its content, so the things under it move with it
        _tileAction = view.Tile.Action;

        _header.SetStatus(UpdateAdvisor.DescribeConnection(_online, _version));
        _folderTile.Set("RESKATE FOLDER", _folder ?? "Click to choose your ReSkate folder");
        _zipTile.Set(
            "TRAINER ZIP",
            _zipPath is null ? "Drop a .zip here, or click to browse"
                : _zipFromOnline ? "Downloaded from GitHub"
                : Path.GetFileName(_zipPath),
            clearable: _zipPath is not null && !_installing);
    }

    private void Log(string message) => _log.AppendText(message + "\r\n");

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    // ---------- Online updates ----------

    // Only on start-up and when the user asks (the Check again button, or a click on the GitHub chip). Never on a timer.
    private async Task RefreshOnlineAsync()
    {
        if (_installing || _checkingOnline) return;

        _online = OnlineStatus.Checking;
        Render();
        await CheckOnlineAsync();
    }

    private async Task CheckOnlineAsync()
    {
        if (_checkingOnline) return;
        _checkingOnline = true;
        ManifestReadResult result;
        try
        {
            result = await TrainerDownloader.FetchManifestAsync(_http);
        }
        finally
        {
            _checkingOnline = false;
        }
        if (IsDisposed) return;

        _manifest = result.Manifest;
        _online = result.Manifest is null ? OnlineStatus.Unavailable : OnlineStatus.Reachable;
        Log(UpdateAdvisor.DescribeManifestCheck(result));
        if (result.Manifest is not null)
        {
            foreach (var problem in result.Problems) Log("  " + problem);
        }

        _offer = UpdateAdvisor.DescribeOnlineOffer(_manifest, _folderInfo);
        _appOffer = UpdateAdvisor.DescribeAppOffer(_manifest, _version, AppUpdater.CanUpdateInPlace(Environment.ProcessPath));
        if (_appOffer is { } newer) Log($"A newer version of the updater is available: {newer.CurrentVersion} to {newer.Entry.Version}.");
        Render();
    }

    private async Task OnTileClickAsync()
    {
        if (_installing) return;
        switch (_tileAction)
        {
            case TileAction.DownloadAndInstall:
                await OnlineInstallAsync();
                break;
            case TileAction.InstallZip:
                await InstallAsync();
                break;
            case TileAction.UpdateApp:
                await UpdateAppAsync();
                break;
            case TileAction.UseSuggestedFolder when _suggestedFolder is { } suggested:
                ChooseFolder(suggested);
                break;
            case TileAction.Launch:
                Launch();
                break;
            case TileAction.CloseGameAndRetry:
                await CloseGameAndRetryAsync();
                break;
        }
    }

    // ---------- The small buttons on the status card ----------

    private async Task OnLinkClickAsync(SquareButton button)
    {
        if (_installing || button.Tag is not CardLink link) return;
        switch (link.Action)
        {
            case LinkAction.CheckAgain:
                await RefreshOnlineAsync();
                break;
            case LinkAction.RestorePrevious:
                await RestoreAsync();
                break;
            case LinkAction.ReportProblem:
                ReportProblem();
                break;
            case LinkAction.OpenFolder when link.Target is { } folder && Directory.Exists(folder):
                OpenWithWindows(folder);
                break;
            case LinkAction.OpenUrl when OnlineSource.IsTrustedPageUrl(link.Target):
                OpenWithWindows(link.Target!);
                break;
        }
    }

    // Hands a folder or an address to Windows, which opens it in Explorer or the browser. Only trusted addresses get here.
    private void OpenWithWindows(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log("Couldn't open " + target + ": " + ex.Message);
        }
    }

    private void ReportProblem() =>
        OpenWithWindows(UpdateAdvisor.DescribeProblemReportUrl(_version, RuntimeInformation.OSDescription));

    private void Launch()
    {
        if (_folder is not { } folder) return;

        var failure = GameLauncher.Start(folder, out var detail);
        if (failure == LaunchFailure.None)
        {
            Log($"Started {ReSkateFiles.Launcher}");
            _result = UpdateAdvisor.DescribeLaunched();
        }
        else
        {
            Log($"Couldn't start {ReSkateFiles.Launcher}: {detail ?? failure.ToString()}");
            _result = UpdateAdvisor.DescribeLaunchFailure(failure, detail);
        }
        Render();
    }

    /// <summary>
    /// The install was blocked by the game. Looks again at what is running (the user may have closed it already), asks before
    /// closing anything, and then carries on with the install that was waiting.
    /// </summary>
    private async Task CloseGameAndRetryAsync()
    {
        if (_installing || _folder is not { } folder) return;

        _installing = true;
        var running = await Task.Run(() => GameProcesses.FindUsing(folder));
        _installing = false;

        if (running.Count > 0)
        {
            if (!Confirm(UpdateAdvisor.DescribeCloseGamePrompt(running))) return;

            _installing = true;
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Closing, string.Join(", ", running.Select(p => p.Name)));
            Render();
            Log("Closing " + string.Join(", ", running.Select(p => p.Name)));

            var stillRunning = await Task.Run(() => GameProcesses.Close(running, TimeSpan.FromSeconds(6)));

            _installing = false;
            _busyView = null;
            if (stillRunning.Count > 0)
            {
                Log("Couldn't close " + string.Join(", ", stillRunning.Select(p => p.Name)));
                _result = UpdateAdvisor.DescribeCloseGameFailed(stillRunning);
                Render();
                return;
            }
            Log("Closed.");
        }

        // Whatever was waiting goes ahead, without asking the same questions twice.
        if (_retry == Retry.Restore) await RestoreAsync(confirm: false);
        else await InstallAsync(clearLog: false, confirm: false);
    }

    /// <summary>Puts back the files the last install replaced. The files being replaced are saved first, so it can be undone.</summary>
    private async Task RestoreAsync(bool confirm = true)
    {
        if (_installing || _folder is not { } folder || _folderInfo?.PreviousBackup is not { } backup) return;
        if (confirm && !Confirm(UpdateAdvisor.DescribeRestorePrompt(backup))) return;

        _installing = true;
        _retry = Retry.Restore;
        _result = null;
        _log.Clear();
        _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Installing, backup.Version?.DisplayName ?? "your previous files");
        Render();
        Log($"Putting back the files saved in {backup.Path}");

        try
        {
            var progress = new Progress<string>(Log);
            var (result, running) = await RunInstallJobAsync(() => TrainerInstaller.Restore(folder, backup, progress), folder);

            Log("");
            LogResult(result);
            _result = UpdateAdvisor.DescribeRestoreResult(result, backup, running);
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
            _result = new Advice("Something went wrong: " + ex.Message, Severity.Error);
        }
        finally
        {
            _installing = false;
            _busyView = null;
        }

        _ = InspectFolderAsync(folder); // show the version that is installed now, and the backup it made
        Render();
    }

    // Runs an install on a worker thread. If the game blocked it, also finds out which programs to name.
    private static async Task<(InstallResult Result, IReadOnlyList<RunningProcess> Running)> RunInstallJobAsync(Func<InstallResult> job, string folder)
    {
        var result = await Task.Run(job);
        IReadOnlyList<RunningProcess> running = Array.Empty<RunningProcess>();
        if (result is { Outcome: InstallOutcome.Blocked, BlockedBy: BlockReason.FilesInUse })
            running = await Task.Run(() => GameProcesses.FindUsing(folder));
        return (result, running);
    }

    /// <summary>
    /// Downloads the newer version of this app, checks it against latest.json, swaps it in for the running exe and starts it.
    /// The current exe is only touched at the very last step, and is put back if that step fails.
    /// </summary>
    private async Task UpdateAppAsync()
    {
        if (_installing || _appOffer is not { } offer || Environment.ProcessPath is not { } exePath || !AppUpdater.CanUpdateInPlace(exePath)) return;

        var app = offer.Entry;
        string what = "ReSkate Trainer Updater " + app.Version;
        _installing = true;
        _result = null;
        _log.Clear();
        Log($"Downloading {what} from {app.Url}");
        _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Downloading, what, new DownloadProgress(0, app.SizeBytes));
        Render();

        // Progress<T> created here posts its reports back to this (UI) thread.
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (!_installing || _busyView is not { Pill: "DOWNLOADING" }) return;
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Downloading, what, p);
            Render();
        });

        Advice? failure = null;
        string? staged = null;
        var download = await TrainerDownloader.DownloadAsync(_http, app, progress);

        if (!download.Succeeded)
        {
            Log("Download failed: " + download.Detail);
            failure = UpdateAdvisor.DescribeDownloadFailure(download.Failure);
        }
        else
        {
            Log(download.Detail ?? "Download verified.");
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Unpacking, what);
            Render();
            try
            {
                var result = await Task.Run(() => AppUpdater.Stage(download.ZipPath!, exePath, app.Version));
                if (result.Succeeded)
                {
                    staged = result.ExePath;
                }
                else
                {
                    Log("Refused: " + result.Detail);
                    failure = UpdateAdvisor.DescribeAppUpdateFailure(result.Failure);
                }
            }
            finally
            {
                TrainerDownloader.DeleteDownloadFolder(download.ZipPath!);
            }
        }

        if (staged is not null)
        {
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Restarting, what);
            Render();

            var (swapFailure, detail) = AppUpdater.Swap(exePath, staged);
            if (swapFailure == AppUpdateFailure.None)
            {
                Log("The new version is in place. Restarting.");
                try
                {
                    Process.Start(new ProcessStartInfo(exePath, "--updated")
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                    });
                    Close(); // this copy exits; the new one deletes the old exe's leftover
                    return;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // The swap worked, so the update is done. It just has to be opened by hand.
                    Log("Couldn't start the new version: " + ex.Message);
                    failure = UpdateAdvisor.DescribeAppRestartFailed(app.Version);
                }
            }
            else
            {
                Log("Couldn't swap in the new version: " + detail);
                AppUpdater.Discard(staged);
                failure = UpdateAdvisor.DescribeAppUpdateFailure(swapFailure);
            }
        }

        _installing = false;
        _busyView = null;
        _result = failure;
        Render();
    }

    /// <summary>
    /// Downloads the offered zip, checks it against latest.json, then hands it to the normal install, exactly as if
    /// the user had dropped it. Nothing in the ReSkate folder is touched until every check has passed.
    /// </summary>
    private async Task OnlineInstallAsync()
    {
        if (_installing || _checkingFolder || _offer is not { CanDownload: true, Entry: { } entry } || _folder is null) return;

        string what = "ReSkate Trainer " + entry.TrainerText;
        _installing = true;
        _result = null;
        _log.Clear();
        Log($"Downloading {what} from {entry.Url}");
        _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Downloading, what, new DownloadProgress(0, entry.SizeBytes));
        Render();

        // Progress<T> created here posts its reports back to this (UI) thread.
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (!_installing || _busyView is not { Pill: "DOWNLOADING" }) return;
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Downloading, what, p);
            Render();
        });

        TrainerPackage? package = null;
        Advice? failure = null;
        var download = await TrainerDownloader.DownloadAsync(_http, entry, progress);

        if (!download.Succeeded)
        {
            Log("Download failed: " + download.Detail);
            failure = UpdateAdvisor.DescribeDownloadFailure(download.Failure);
        }
        else
        {
            Log(download.Detail ?? "Download verified.");
            _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Unpacking, what);
            Render();
            try
            {
                package = await Task.Run(() => TrainerPackageLoader.Load(download.ZipPath!));
                if (TrainerDownloader.CheckPackageMatches(entry, package) is { } mismatch)
                {
                    Log("Refused: " + mismatch);
                    failure = UpdateAdvisor.DescribePackageMismatch(mismatch);
                    package.Dispose();
                    package = null;
                }
            }
            catch (Exception ex)
            {
                Log("Refused: " + ex.Message);
                failure = UpdateAdvisor.DescribePackageMismatch(ex.Message);
                package?.Dispose();
                package = null;
            }
            finally
            {
                // The package is unpacked to its own temp folder, so the downloaded zip is no longer needed.
                TrainerDownloader.DeleteDownloadFolder(download.ZipPath!);
            }
        }

        _installing = false;
        _busyView = null;

        if (package is null)
        {
            _result = failure ?? UpdateAdvisor.DescribeDownloadFailure(DownloadFailure.Network);
            Render();
            return;
        }

        // From here on it is the ordinary install of a checked zip, with the same confirmations, backup and rollback.
        ClearZip();
        _zipPath = download.ZipPath;
        _package = package;
        _zipFromOnline = true;
        Render();

        bool started = await InstallAsync(clearLog: false);
        if (!started && _zipFromOnline)
        {
            // The user said no at a confirmation. Forget the download so the screen offers it again.
            ClearZip();
            Render();
        }
    }

    // ---------- Installing ----------

    /// <summary>Installs the current package. Returns false if it didn't start, for example when the user said no.</summary>
    private async Task<bool> InstallAsync(bool clearLog = true, bool confirm = true)
    {
        if (_installing) return false;
        if (!UpdateAdvisor.Evaluate(CurrentContext()).CanInstall) return false;
        if (_package is not { Version: { } incoming } package || _folder is not { } folder || _folderInfo is not { } folderInfo) return false;

        bool backup = _backupRow.Checked;

        // Ask before anything unusual. Not again when this is a retry the user already said yes to.
        if (confirm)
        {
            if (UpdateAdvisor.IsDowngrade(folderInfo.Installed, incoming) &&
                !Confirm($"This zip ({incoming.TrainerText}) is older than the version you have ({folderInfo.Installed!.TrainerText}).\r\n\r\nInstall the older version anyway?"))
            {
                return false;
            }

            if (UpdateAdvisor.DescribeEditionSwitchPrompt(folderInfo.Installed, incoming) is { } switchPrompt && !Confirm(switchPrompt))
                return false;

            if (!folderInfo.HasLauncher && !folderInfo.IsEmpty &&
                !Confirm($"There's no ReSkate in this folder (no {ReSkateFiles.Launcher}) and it isn't empty.\r\n\r\nInstall ReSkate Trainer into it anyway?"))
            {
                return false;
            }

            if (!backup &&
                !Confirm("Backups are turned off, so your old files can't be restored afterwards.\r\n\r\nContinue?"))
            {
                return false;
            }
        }

        _installing = true;
        _retry = Retry.Install;
        _result = null;
        if (clearLog) _log.Clear();
        _busyView = UpdateAdvisor.DescribeBusy(BusyStage.Installing, incoming.DisplayName);
        Render();
        Log($"Installing {incoming.DisplayName} into {folder}");
        foreach (var note in package.Notes) Log(note);

        bool finished = false;
        try
        {
            // Progress<T> created here posts its reports back to this (UI) thread.
            var progress = new Progress<string>(Log);
            var (result, running) = await RunInstallJobAsync(() => TrainerInstaller.Install(package.RootFolder, folder, backup, progress), folder);

            Log("");
            LogResult(result);
            _result = UpdateAdvisor.DescribeResult(result, incoming, running);
            finished = result.Outcome is InstallOutcome.Installed or InstallOutcome.AlreadyUpToDate;
        }
        catch (Exception ex)
        {
            // Install reports its own failures through the result; this is for anything unexpected.
            Log("Error: " + ex.Message);
            _result = new Advice("Something went wrong: " + ex.Message, Severity.Error);
        }
        finally
        {
            _installing = false;
            _busyView = null;
        }

        // A finished install is done with its zip: clear it so the screen shows what is installed, not a second Install.
        if (finished) ClearZip();
        Render();
        _ = InspectFolderAsync(folder); // show the version that is installed now, and the backup this install made
        return true;
    }

    private void LogResult(InstallResult result)
    {
        switch (result.Outcome)
        {
            case InstallOutcome.Installed:
                Log($"Done: {result.Replaced} replaced, {result.Added} added, {result.Unchanged} already up to date.");
                if (result.BackupFolder is not null) Log("Your old files are saved in: " + result.BackupFolder);
                break;

            case InstallOutcome.AlreadyUpToDate:
                Log("Every file already matches this version.");
                break;

            case InstallOutcome.Blocked:
                Log("Nothing was changed. Problems found before starting:");
                foreach (var problem in result.Problems) Log("  - " + problem);
                break;

            case InstallOutcome.RestoreFailed:
                Log("Some of your original files could not be put back.");
                Log("Copies of them are in: " + result.RescueFolder);
                break;
        }
    }
}
