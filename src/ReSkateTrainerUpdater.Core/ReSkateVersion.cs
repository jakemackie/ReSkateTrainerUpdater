namespace ReSkateTrainerUpdater.Core;

/// <summary>What is known about one ReSkate.dll or ReSkateLauncher.exe.</summary>
/// <param name="BaseVersion">The ReSkate version from the file properties, for example 2.1.1.</param>
/// <param name="ProductName">The product name from the file properties. Should be "ReSkate".</param>
/// <param name="TrainerVersion">The Trainer version, for example 0.9.2. Null for plain ReSkate without the Trainer.</param>
/// <param name="TrainerBuild">The Trainer build, for example 578f597.</param>
/// <param name="Edition">The Trainer edition, for example Tournament. Null for the standard edition.</param>
public sealed record ReSkateVersion(
    string? BaseVersion,
    string? ProductName,
    string? TrainerVersion,
    string? TrainerBuild,
    string? Edition)
{
    public bool HasTrainer => TrainerVersion is not null;

    /// <summary>"0.9.2 (Tournament)", or just "0.9.2" for the standard edition. Empty without a Trainer.</summary>
    public string TrainerText =>
        TrainerVersion is null ? "" : Edition is null ? TrainerVersion : $"{TrainerVersion} ({Edition})";

    /// <summary>"ReSkate Trainer 0.9.2 (Tournament)", or "ReSkate 2.1.1" when there is no Trainer.</summary>
    public string DisplayName => HasTrainer ? $"ReSkate Trainer {TrainerText}" : $"ReSkate {BaseVersion}";
}
