namespace ReSkateTrainerUpdater.Ui;

/// <summary>Names and text shown in more than one place.</summary>
internal static class AppInfo
{
    public const string Name = "ReSkate Trainer Updater";
    public const string Tagline = "Installs the newest ReSkate Trainer into your ReSkate folder.";

    /// <summary>
    /// The identity the setup (installer/ReSkateTrainerUpdater.iss) gives the app. Windows files the Add/Remove Programs
    /// entry under it, and a newer setup uses it to replace an older install. It must never change, and must match AppId
    /// in the .iss. A test checks that.
    /// </summary>
    public const string InstallerAppId = "{A0B385CA-E8B7-4191-93AA-E09E62F2C52B}";
}
