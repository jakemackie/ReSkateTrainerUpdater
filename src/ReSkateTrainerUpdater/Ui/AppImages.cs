namespace ReSkateTrainerUpdater.Ui;

/// <summary>Pictures embedded in the exe.</summary>
internal static class AppImages
{
    // Must match the LogicalName of the EmbeddedResource in the project file.
    private const string ProfileResource = "profile.png";

    private static Bitmap? _profile;
    private static bool _profileTried;

    /// <summary>The creator's profile picture, or null if it can't be loaded. Loaded once and kept for the life of the app.</summary>
    public static Bitmap? Profile
    {
        get
        {
            if (_profileTried) return _profile;
            _profileTried = true;
            try
            {
                using var stream = typeof(AppImages).Assembly.GetManifestResourceStream(ProfileResource);
                if (stream is null) return null;

                // Bitmap needs its stream open while it is used, so copy it into memory-backed pixels.
                using var loaded = new Bitmap(stream);
                _profile = new Bitmap(loaded);
            }
            catch (ArgumentException) { _profile = null; }
            return _profile;
        }
    }
}
