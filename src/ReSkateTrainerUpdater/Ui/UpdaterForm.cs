using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui.Controls;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>
/// The one window: pick the ReSkate folder, then either click the online Update/Install button in the banner,
/// or drop a Trainer zip and press Install. Both routes end in the same InstallAsync.
/// This class only handles the window itself. What to say, what to download and how to install lives in Core.
/// </summary>
internal sealed class UpdaterForm : Form
{
    private readonly UpdaterSettings _settings = UpdaterSettings.Load();

    private readonly HeaderBar _header = new();
    private readonly UpdateBanner _banner = new();
    private readonly DropZone _drop = new();
    private readonly PathCard _pathCard = new();
    private readonly RoundedButton _browseButton = new() { Text = "Change" };
    private readonly OptionRow _backupRow = new()
    {
        Title = "Back up replaced files",
        Description = "Keeps a copy of your old files so you can go back.",
    };
    private readonly RoundedButton _installButton = new() { Text = InstallText, Primary = true };
    private readonly ProgressStrip _progress = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();

    private const string InstallText = "Install update";

    // The zip.
    private string? _zipPath;
    private string? _zipProblem;
    private TrainerPackage? _package;
    private bool _checkingZip;
    private int _zipGeneration;

    // The ReSkate folder.
    private string? _folder;
    private InstallFolderInfo? _folderInfo;
    private bool _checkingFolder;
    private int _folderGeneration;

    // Online updates. One HTTP client for the app's lifetime.
    private readonly HttpClient _http = TrainerDownloader.CreateClient();
    private TrainerManifest? _manifest;
    private OnlineOffer _offer = OnlineOffer.Nothing;
    private RowStyle? _bannerRow;
    private const float BannerHeight = 84f;

    // The outcome of the last install, shown until the user changes something.
    private Advice? _result;
    private bool _installing;

    public UpdaterForm()
    {
        Text = AppInfo.Name;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(580, 780);
        MinimumSize = new Size(520, 720);
        StartPosition = FormStartPosition.CenterScreen;

        if (AppIcon.Load() is { } icon) Icon = icon;

        BuildLayout();

        // Restore what the user had last time.
        _backupRow.SetCheckedImmediate(_settings.BackupReplacedFiles);
        if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
        {
            _folder = _settings.LastFolder;
            _pathCard.FolderPath = _folder;
        }

        _drop.Click += (_, _) => { if (!_installing) BrowseForZip(); };
        _pathCard.Click += (_, _) => { if (!_installing) BrowseForFolder(); };
        _browseButton.Click += (_, _) => { if (!_installing) BrowseForFolder(); };
        _installButton.Click += async (_, _) => await InstallAsync();
        _banner.ButtonClick += async (_, _) => await OnlineInstallAsync();
        _backupRow.CheckedChanged += (_, _) =>
        {
            _settings.BackupReplacedFiles = _backupRow.Checked;
            _settings.Save();
        };

        EnableDropOn(this);
        RefreshStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = Task.Run(TrainerPackageLoader.CleanUpStaleTempFolders);

        // One quiet request for latest.json. Any failure just leaves the banner hidden.
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
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Bg,
            Padding = new Padding(26, 16, 26, 20),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Folder row: the folder card plus a Change button.
        var folderRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Bg,
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
        folderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _pathCard.Dock = DockStyle.Fill;
        _pathCard.Margin = new Padding(0, 0, 10, 0);
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Margin = Padding.Empty;
        folderRow.Controls.Add(_pathCard, 0, 0);
        folderRow.Controls.Add(_browseButton, 1, 0);

        // Status line (can wrap onto two lines).
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.TopLeft;
        _status.BackColor = Theme.Bg;
        _status.ForeColor = Theme.Muted;
        _status.Font = Theme.Body;
        _status.Padding = new Padding(2, 4, 0, 0);
        _status.UseMnemonic = false;

        // Activity log inside a rounded card.
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
        var logCard = new CardPanel();
        logCard.Controls.Add(_log);

        AddRow(layout, _header, 64);
        _bannerRow = AddRow(layout, _banner, BannerHeight, new Padding(0, 4, 0, 6));
        ShowBanner(false);
        AddRow(layout, SectionLabel("UPDATE FILE (.ZIP)"), 28);
        AddRow(layout, _drop, 104);
        AddRow(layout, SectionLabel("YOUR RESKATE FOLDER"), 34);
        AddRow(layout, folderRow, 86, new Padding(0, 0, 0, 14));
        AddRow(layout, _backupRow, 62, new Padding(0, 0, 0, 8));
        AddRow(layout, _installButton, 60, new Padding(0, 10, 0, 0));
        AddRow(layout, _progress, 10);
        AddRow(layout, _status, 48);
        AddRow(layout, SectionLabel("ACTIVITY"), 28);
        AddRow(layout, logCard, 0); // 0 = take all the remaining space

        Controls.Add(layout);
    }

    private static RowStyle AddRow(TableLayoutPanel table, Control control, float height, Padding? margin = null)
    {
        var style = height > 0
            ? new RowStyle(SizeType.Absolute, height)
            : new RowStyle(SizeType.Percent, 100f);
        table.RowStyles.Add(style);
        control.Dock = DockStyle.Fill;
        control.Margin = margin ?? Padding.Empty;
        table.RowCount = table.RowStyles.Count;
        table.Controls.Add(control, 0, table.RowStyles.Count - 1);
        return style;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        ForeColor = Theme.Muted,
        BackColor = Theme.Bg,
        Font = Theme.Label,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(2, 0, 0, 6),
        UseMnemonic = false,
    };

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
            _drop.Highlight = true;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, EventArgs e) => _drop.Highlight = false;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        _drop.Highlight = false;
        if (_installing) return;

        if (TryGetDroppedZip(e.Data, out var zipPath))
        {
            _ = LoadZipAsync(zipPath);
        }
        else
        {
            _result = new Advice("That doesn't look like a .zip file. Drop the ReSkate Trainer zip you downloaded.", Severity.Warning);
            RefreshStatus();
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

    private async Task LoadZipAsync(string path)
    {
        int generation = ++_zipGeneration;
        _zipPath = path;
        _zipProblem = null;
        _result = null;
        DisposePackage();

        _checkingZip = true;
        _drop.SetState(DropState.Working, "Checking " + Path.GetFileName(path) + "...", "Looking inside the zip");
        RefreshStatus();

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
        ShowPackageInDropZone(path);
        RefreshStatus();
    }

    private void ShowPackageInDropZone(string path, string? downloadedFrom = null)
    {
        var fileName = Path.GetFileName(path);

        if (_package is null)
        {
            _drop.SetState(DropState.Problem, "Couldn't read this zip", fileName + " · click to choose another");
            return;
        }

        if (!_package.IsValid || _package.Version is not { } version)
        {
            _drop.SetState(DropState.Problem, "This isn't a ReSkate Trainer update", fileName + " · click to choose another");
            return;
        }

        var details = new List<string>();
        string title;
        if (version.HasTrainer)
        {
            title = $"ReSkate Trainer {version.TrainerVersion}";
            if (version.Edition is not null) details.Add(version.Edition + " Edition");
            if (version.BaseVersion is not null) details.Add("built on ReSkate " + version.BaseVersion);
        }
        else
        {
            title = $"ReSkate {version.BaseVersion}";
            details.Add("no Trainer found in this zip");
        }
        details.Add(downloadedFrom ?? ByteSize.Format(_package.ZipSize));

        _drop.SetState(DropState.Ready, title, string.Join(" · ", details));
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
        _pathCard.FolderPath = _folder;
        _settings.LastFolder = _folder;
        _settings.Save();
        _ = InspectFolderAsync(_folder);
    }

    private async Task InspectFolderAsync(string path)
    {
        int generation = ++_folderGeneration;
        _checkingFolder = true;
        _folderInfo = null;
        _pathCard.SetInstalled("Checking this folder...", Theme.Muted);
        RefreshOffer();

        InstallFolderInfo? info = null;
        string? problem = null;
        try
        {
            info = await Task.Run(() => InstallFolderInspector.Inspect(path));
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the window must leave its "checking" state and tell the user.
            problem = ex.Message;
        }

        if (generation != _folderGeneration) return;

        _checkingFolder = false;
        _folderInfo = info;

        if (info is null)
        {
            _pathCard.SetInstalled("Couldn't read this folder: " + problem, Theme.Danger);
        }
        else
        {
            var (text, severity) = UpdateAdvisor.DescribeFolder(info);
            _pathCard.SetInstalled(text, Theme.ForSeverity(severity));
        }
        RefreshOffer();
    }

    // ---------- Status ----------

    private UpdateContext CurrentContext() =>
        new(_checkingZip, _zipPath, _zipProblem, _package, _folder, _checkingFolder, _folderInfo, _offer);

    private void RefreshStatus()
    {
        if (_installing) return;

        var advice = UpdateAdvisor.Evaluate(CurrentContext());
        _installButton.Enabled = advice.CanInstall;

        var shown = _result ?? advice;
        _status.Text = shown.Message;
        _status.ForeColor = Theme.ForSeverity(shown.Severity);
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
        Log(UpdateAdvisor.DescribeManifestCheck(result));
        if (result.Manifest is not null)
        {
            foreach (var problem in result.Problems) Log("  " + problem);
        }
        RefreshOffer();
    }

    // Works out what the banner offers for the current folder, then refreshes the status line.
    private void RefreshOffer()
    {
        if (!_installing)
        {
            _offer = UpdateAdvisor.DescribeOnlineOffer(_manifest, _checkingFolder ? null : _folderInfo);
            if (_offer.CanDownload) _banner.SetOffer(_offer.Title, _offer.Detail, _offer.ButtonText);
            ShowBanner(_offer.CanDownload);
        }
        RefreshStatus();
    }

    private void ShowBanner(bool show)
    {
        if (_bannerRow is null) return;
        _banner.Visible = show;
        _bannerRow.Height = show ? BannerHeight : 0f;
    }

    /// <summary>
    /// Downloads the offered zip, checks it against latest.json, then hands it to the normal install, exactly as if
    /// the user had dropped it. Nothing in the ReSkate folder is touched until every check has passed.
    /// </summary>
    private async Task OnlineInstallAsync()
    {
        if (_installing || _offer is not { CanDownload: true, Entry: { } entry } || _folder is null) return;

        string buttonText = _offer.ButtonText;
        _installing = true;
        _result = null;
        _installButton.Enabled = false;
        _banner.ButtonEnabled = false;
        _banner.SetButtonText("Downloading...");
        _progress.Active = true;
        _log.Clear();
        Log($"Downloading ReSkate Trainer {entry.TrainerText} from {entry.Url}");
        ShowStatus(UpdateAdvisor.DescribeDownloadProgress(entry, new DownloadProgress(0, entry.SizeBytes)));

        // Progress<T> created here posts its reports back to this (UI) thread.
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (_installing) ShowStatus(UpdateAdvisor.DescribeDownloadProgress(entry, p));
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
        _progress.Active = false;
        _banner.SetButtonText(buttonText);
        _banner.ButtonEnabled = true;

        if (package is null)
        {
            _result = failure ?? UpdateAdvisor.DescribeDownloadFailure(DownloadFailure.Network);
            RefreshStatus();
            return;
        }

        // From here on it is the ordinary install of a checked zip, with the same confirmations, backup and rollback.
        _zipGeneration++;
        DisposePackage();
        _checkingZip = false;
        _zipPath = download.ZipPath;
        _zipProblem = null;
        _package = package;
        ShowPackageInDropZone(download.ZipPath!, "downloaded from GitHub");
        RefreshStatus();

        await InstallAsync(clearLog: false);
    }


    // ---------- Installing ----------

    /// <summary>Installs the current package. Both the Install button and the online banner end up here.</summary>
    private async Task InstallAsync(bool clearLog = true)
    {
        if (_installing) return;
        if (!UpdateAdvisor.Evaluate(CurrentContext()).CanInstall) return;
        if (_package is not { Version: { } incoming } package || _folder is not { } folder || _folderInfo is not { } folderInfo) return;

        bool backup = _backupRow.Checked;

        // Ask before anything unusual.
        if (UpdateAdvisor.IsDowngrade(folderInfo.Installed, incoming) &&
            !Confirm($"This zip ({incoming.TrainerText}) is older than the version you have ({folderInfo.Installed!.TrainerText}).\r\n\r\nInstall the older version anyway?"))
        {
            return;
        }

        if (!folderInfo.HasLauncher && !folderInfo.IsEmpty &&
            !Confirm($"There's no ReSkate in this folder (no {ReSkateFiles.Launcher}) and it isn't empty.\r\n\r\nInstall ReSkate Trainer into it anyway?"))
        {
            return;
        }

        if (!backup &&
            !Confirm("Backups are turned off, so your old files can't be restored afterwards.\r\n\r\nContinue?"))
        {
            return;
        }

        _installing = true;
        _result = null;
        _installButton.Enabled = false;
        _installButton.Text = "Installing...";
        _banner.ButtonEnabled = false;
        _progress.Active = true;
        if (clearLog) _log.Clear();
        ShowStatus(new Advice("Installing... please don't close the app.", Severity.Working));
        Log($"Installing {incoming.DisplayName} into {folder}");
        foreach (var note in package.Notes) Log(note);

        try
        {
            // Progress<T> created here posts its reports back to this (UI) thread.
            var progress = new Progress<string>(Log);
            var result = await Task.Run(() => TrainerInstaller.Install(package.RootFolder, folder, backup, progress));

            Log("");
            LogResult(result);
            _result = UpdateAdvisor.DescribeResult(result, incoming);
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
            _progress.Active = false;
            _installButton.Text = InstallText;
            _banner.ButtonEnabled = true;
        }

        RefreshStatus();
        _ = InspectFolderAsync(folder); // show the version that is installed now
    }

    private void ShowStatus(Advice advice)
    {
        _status.Text = advice.Message;
        _status.ForeColor = Theme.ForSeverity(advice.Severity);
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
