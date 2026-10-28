using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ReSkateTrainerUpdater.Core;

/// <summary>What <see cref="ManifestSignature.Verify"/> found.</summary>
/// <param name="IsValid">True only when the signature is present and was made by a trusted key.</param>
/// <param name="Payload">The text that was signed (latest.json without its signature), when the layout was readable.</param>
/// <param name="Problem">Why it isn't valid, for the activity log. Null when valid.</param>
public sealed record SignatureCheck(bool IsValid, string? Payload, string? Problem);

/// <summary>
/// The signature at the end of latest.json. Every installed copy checks it before believing a word of the file, so
/// someone who can change the file on GitHub (or in between) still can't offer a different download.
///
/// The scheme is ECDSA with the P-256 curve and SHA-256, signature in the IEEE P1363 layout (r then s, 64 bytes),
/// written as base64 in a final "signature" member. The signed bytes are the file's canonical text
/// (<see cref="TrainerManifest.ToJson"/>) exactly as it is without that member. Older apps ignore the extra member,
/// because they ignore every field they don't know. See docs/ONLINE-UPDATES.md.
/// </summary>
public static class ManifestSignature
{
    /// <summary>
    /// The public keys (base64 SubjectPublicKeyInfo) whose signatures this app accepts. A signature from any one is
    /// enough, so a spare key kept offline can replace a lost or leaked one: ship an app that lists the new key, then
    /// sign with it. The private halves are never in this repository. Create a pair with the manifest tool's keygen.
    /// </summary>
    public static IReadOnlyList<string> TrustedPublicKeys { get; } = new[]
    {
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE5gsNWUb1xVTF1RpR2G7q88u7dIrwJf86BWV5++azqytNqi/xwPMoyAiRH4rE4suUjhxSoxeWt3Jrz/kIQSzHzA==",
    };

    // How the member sits at the end of the file. Both writing and reading depend on this exact layout.
    private const string MemberStart = ",\n  \"signature\": \"";
    private const string FileEnd = "\n}\n";
    private static readonly Regex SignatureTail = new("^[A-Za-z0-9+/]{86}==\"\n}\n\\z", RegexOptions.CultureInvariant);

    /// <summary>Adds the signature member to canonical, unsigned manifest text.</summary>
    public static string Sign(string unsignedJson, ECDsa privateKey)
    {
        if (!unsignedJson.EndsWith(FileEnd, StringComparison.Ordinal) || unsignedJson.Contains('\r'))
            throw new ArgumentException("Expected the canonical manifest text (LF endings, final newline).", nameof(unsignedJson));

        var signature = privateKey.SignData(Encoding.UTF8.GetBytes(unsignedJson), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return unsignedJson[..^FileEnd.Length] + MemberStart + Convert.ToBase64String(signature) + "\"" + FileEnd;
    }

    /// <summary>
    /// Checks the signature on latest.json text. Never throws. CRLF line endings are tolerated, because a copy checked
    /// out on Windows may have them; the bytes GitHub serves always use LF.
    /// </summary>
    public static SignatureCheck Verify(string text, IEnumerable<string>? publicKeys = null)
    {
        text = text.Replace("\r\n", "\n");

        int at = text.LastIndexOf(MemberStart, StringComparison.Ordinal);
        if (at < 0) return new SignatureCheck(false, null, "latest.json has no signature.");

        string tail = text[(at + MemberStart.Length)..];
        if (!SignatureTail.IsMatch(tail))
            return new SignatureCheck(false, null, "latest.json's signature is not in the expected layout.");

        string payload = text[..at] + FileEnd;
        byte[] signature;
        try { signature = Convert.FromBase64String(tail[..88]); }
        catch (FormatException) { return new SignatureCheck(false, payload, "latest.json's signature is not valid base64."); }

        var data = Encoding.UTF8.GetBytes(payload);
        foreach (var key in publicKeys ?? TrustedPublicKeys)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
                if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return new SignatureCheck(true, payload, null);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
            {
                // A key that can't be read just doesn't match; try the next one.
            }
        }

        return new SignatureCheck(false, payload, "latest.json's signature doesn't match any key this app trusts, so the file was ignored.");
    }

    /// <summary>A new key pair: the private key (PKCS#8, base64) to keep secret, and the public key (base64) to put in <see cref="TrustedPublicKeys"/>.</summary>
    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Loads a private key made by <see cref="GenerateKeyPair"/>. Throws if it can't be read.</summary>
    public static ECDsa LoadPrivateKey(string base64)
    {
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(base64.Trim()), out _);
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    /// <summary>The public key (base64) that goes with a private key.</summary>
    public static string PublicKeyOf(ECDsa privateKey) => Convert.ToBase64String(privateKey.ExportSubjectPublicKeyInfo());
}
