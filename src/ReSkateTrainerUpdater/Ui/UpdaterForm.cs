using System.ComponentModel;
using System.Diagnostics;
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
internal sealed class UpdaterForm : Form
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
    private readonly SquareButton _logButton = new() { Text = "ACTIVITY LOG", Width = 170, Dark = true };
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
    private bool _checkingFolder;
    private int _folderGeneration;

    // Online updates. One HTTP client for the app's lifetime.
    private readonly HttpClient _http = TrainerDownloader.CreateClient();
    private TrainerManifest? _manifest;
    private OnlineStatus _online = OnlineStatus.Checking;
    private OnlineOffer _offer = OnlineOffer.Nothing;
    private AppOffer? _appOffer;

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
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        // No title bar, like the launcher. The window has a fixed size, so there is no maximise button either.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;

        // Roomy by default, but never bigger than the screen's usable area (a small laptop screen still fits it).
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(1120, area.Width - 60), Math.Min(800, area.Height - 60));
        StartPosition = FormStartPosition.CenterScreen;

        if (AppIcon.Load() is { } icon) Icon = icon;

        BuildLayout();
        MakeDraggable(this);

        // Restore what the user had last time.
        _backupRow.SetCheckedImmediate(_settings.BackupReplacedFiles);
        if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
            _folder = _settings.LastFolder;

        _folderTile.Click += (_, _) => { if (!_installing) BrowseForFolder(); };
        _zipTile.Click += (_, _) => { if (!_installing) BrowseForZip(); };
        _zipTile.ClearClick += (_, _) => { if (!_installing) ClearZipByUser(); };
        _actionTile.Click += async (_, _) => await OnTileClickAsync();
        _logButton.Click += (_, _) => ToggleLog();
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

        // One quiet request for latest.json. Any failure just leaves the screen as it would be offline.
        _ = CheckOnlineAsync();

        // Show which version is already installed in the folder remembered from last time.
        if (_folder is not null) _ = InspectFolderAsync(_folder);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposePackage();
        _http.Dispose();
        base.OnFormClosed(e);
    }

    // ---------- Layout ----------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Bg,
            Padding = new Padding(46, 28, 46, 32),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));

        _header.Dock = DockStyle.Fill;
        _header.Margin = Padding.Empty;
        root.Controls.Add(_header, 0, 0);

        // Body: two columns with a gap between them.
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Left: the status card (or the log in its place), and the backup option under it.
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg, Margin = new Padding(0, 0, 20, 0) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));

        var stack = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Padding.Empty };
        _statusCard.Dock = DockStyle.Fill;
        BuildLogCard();
        stack.Controls.Add(_logCard);
        stack.Controls.Add(_statusCard);
        left.Controls.Add(stack, 0, 0);

        _backupRow.Dock = DockStyle.Fill;
        _backupRow.Margin = new Padding(0, 20, 0, 0);
        left.Controls.Add(_backupRow, 0, 1);

        // Right: the folder, the zip, and the big tile.
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Margin = new Padding(20, 0, 0, 0) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 98f));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 98f));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _folderTile.Dock = DockStyle.Fill;
        _folderTile.Margin = Padding.Empty;
        _zipTile.Dock = DockStyle.Fill;
        _zipTile.Margin = new Padding(0, 20, 0, 0);
        _actionTile.Dock = DockStyle.Fill;
        _actionTile.Margin = new Padding(0, 20, 0, 0);
        right.Controls.Add(_folderTile, 0, 0);
        right.Controls.Add(_zipTile, 0, 1);
        right.Controls.Add(_actionTile, 0, 2);

        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);
        root.Controls.Add(body, 0, 1);

        // Footer: the log button, like CHANGELOGS in the launcher.
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _logButton.Dock = DockStyle.Fill;
        _logButton.Margin = new Padding(0, 20, 0, 0);
        footer.Controls.Add(_logButton, 0, 0);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        // Minimise and close, flush in the top-right corner and above everything else.
        _windowButtons.Location = new Point(ClientSize.Width - _windowButtons.Width, 0);
        _windowButtons.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(_windowButtons);
        _windowButtons.BringToFront();
    }

    private void BuildLogCard()
    {
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Card;
        _log.ForeColor = Theme.Muted;
        _log.Font = Theme.Mono;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.TabStop = false;
        _log.HandleCreated += (_, _) => NativeMethods.UseDarkScrollbars(_log.Handle);

        _logCard.Dock = DockStyle.Fill;
        _logCard.Padding = new Padding(30, 66, 16, 20);
        _logCard.Controls.Add(_log);
    }

    private void ToggleLog()
    {
        bool show = !_logCard.Visible;
        _logCard.Visible = show;
        _statusCard.Visible = !show;
        _logButton.Text = show ? "BACK TO STATUS" : "ACTIVITY LOG";
    }

    // Without a title bar, the window is moved by dragging any empty part of it, like the launcher.
    private void MakeDraggable(Control control)
    {
        if (control is Form or TableLayoutPanel or Panel or HeaderBar or StatusCard)
            control.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragWindow(Handle); };
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

        _folder = dialog.SelectedPath;
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
        _offer = UpdateAdvisor.DescribeOnlineOffer(_manifest, _folderInfo);
        Render();
    }

    // ---------- Drawing the screen ----------

    private UpdateContext CurrentContext() =>
        new(_checkingZip, _zipPath, _zipProblem, _package, _folder, _checkingFolder, _folderInfo, _offer, _online, _appOffer);

    // Draws everything from the current state. The decisions are made in UpdateAdvisor.
    private void Render()
    {
        HomeView view = _busyView ?? UpdateAdvisor.DescribeHome(CurrentContext());
        if (_busyView is null && _result is { } result) view = UpdateAdvisor.WithResult(view, result);

        _statusCard.Show(view);
        _actionTile.Show(view.Tile);
        _tileAction = view.Tile.Action;

        _header.SetChip(UpdateAdvisor.DescribeConnection(_online, _version));
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

    private async Task CheckOnlineAsync()
    {
        var result = await TrainerDownloader.FetchManifestAsync(_http);
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
        }
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
    private async Task<bool> InstallAsync(bool clearLog = true)
    {
        if (_installing) return false;
        if (!UpdateAdvisor.Evaluate(CurrentContext()).CanInstall) return false;
        if (_package is not { Version: { } incoming } package || _folder is not { } folder || _folderInfo is not { } folderInfo) return false;

        bool backup = _backupRow.Checked;

        // Ask before anything unusual.
        if (UpdateAdvisor.IsDowngrade(folderInfo.Installed, incoming) &&
            !Confirm($"This zip ({incoming.TrainerText}) is older than the version you have ({folderInfo.Installed!.TrainerText}).\r\n\r\nInstall the older version anyway?"))
        {
            return false;
        }

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

        _installing = true;
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
            var result = await Task.Run(() => TrainerInstaller.Install(package.RootFolder, folder, backup, progress));

            Log("");
            LogResult(result);
            _result = UpdateAdvisor.DescribeResult(result, incoming);
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
        _ = InspectFolderAsync(folder); // show the version that is installed now
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
