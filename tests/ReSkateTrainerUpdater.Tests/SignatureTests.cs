using System.Security.Cryptography;
using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>The signature on latest.json: what it covers, what it rejects, and that old apps can still read the file.</summary>
internal static class SignatureTests
{
    public static void Run()
    {
        Section("Signed latest.json");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new[] { ManifestSignature.PublicKeyOf(key) };

        var entry = new ManifestEntry("tournament", "0.9.2", "578f597", "2.1.1",
            "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/t.zip", 1000, new string('a', 64));
        var unsigned = new TrainerManifest().With(entry).ToJson();
        var signed = ManifestSignature.Sign(unsigned, key);

        Check("A signed file verifies with the right key", ManifestSignature.Verify(signed, keys).IsValid);
        Check("The signed text is the canonical text plus one final member",
            signed.StartsWith(unsigned[..^3], StringComparison.Ordinal) && signed.EndsWith("\"\n}\n", StringComparison.Ordinal));
        Check("The signed file is still ordinary JSON the existing parser reads",
            TrainerManifest.Parse(signed) is { Manifest: not null } read && read.Problems.Count == 0 && read.Manifest.Find("tournament") == entry);
        Check("The payload handed back is exactly what was signed", ManifestSignature.Verify(signed, keys).Payload == unsigned);
        Check("CRLF line endings (a Windows checkout) still verify", ManifestSignature.Verify(signed.Replace("\n", "\r\n"), keys).IsValid);
        Check("Any one of several trusted keys is enough",
            ManifestSignature.Verify(signed, new[] { ManifestSignature.PublicKeyOf(other), keys[0] }).IsValid);

        Check("Invariant: a signature from another key is refused",
            !ManifestSignature.Verify(signed, new[] { ManifestSignature.PublicKeyOf(other) }).IsValid);
        Check("Invariant: no trusted keys means nothing is accepted", !ManifestSignature.Verify(signed, Array.Empty<string>()).IsValid);
        Check("Invariant: an unsigned file is refused", ManifestSignature.Verify(unsigned, keys) is { IsValid: false, Problem: not null });
        Check("Invariant: changing the download hash after signing is caught",
            !ManifestSignature.Verify(signed.Replace(new string('a', 64), new string('b', 64)), keys).IsValid);
        Check("Invariant: changing the URL after signing is caught",
            !ManifestSignature.Verify(signed.Replace("t.zip", "evil.zip"), keys).IsValid);
        Check("Invariant: adding an entry after signing is caught",
            !ManifestSignature.Verify(signed.Replace("\"editions\": {", "\"editions\": {\n    \"extra\": {},"), keys).IsValid);
        Check("Invariant: a signature copied from an older file onto a different one is caught",
            !ManifestSignature.Verify(ManifestSignature.Sign(unsigned.Replace("0.9.2", "0.9.1"), key).Replace("0.9.1", "0.9.2"), keys).IsValid);
        Check("Text after the signature is refused", !ManifestSignature.Verify(signed + "{}", keys).IsValid);
        Check("A damaged signature is refused", !ManifestSignature.Verify(signed.Replace("\"\n}\n", "x\"\n}\n"), keys).IsValid);
        Check("A truncated file is refused", !ManifestSignature.Verify(signed[..(signed.Length / 2)], keys).IsValid);
        Check("Garbage keys are skipped, not fatal", ManifestSignature.Verify(signed, new[] { "not base64!", "AAAA", keys[0] }).IsValid);

        Section("Signing keys");
        var (privateKey, publicKey) = ManifestSignature.GenerateKeyPair();
        using (var loaded = ManifestSignature.LoadPrivateKey(privateKey))
        {
            Check("A generated private key loads and matches its public key", ManifestSignature.PublicKeyOf(loaded) == publicKey);
            Check("A key made by keygen signs something the matching public key accepts",
                ManifestSignature.Verify(ManifestSignature.Sign(unsigned, loaded), new[] { publicKey }).IsValid);
        }
        bool threw = false;
        try { ManifestSignature.LoadPrivateKey("not a key"); } catch (Exception) { threw = true; }
        Check("A bad private key is reported, not ignored", threw);
        threw = false;
        try { ManifestSignature.Sign("{}", key); } catch (ArgumentException) { threw = true; }
        Check("Only canonical text can be signed", threw);

        Check("The app trusts at least one key, and each is a real P-256 public key",
            ManifestSignature.TrustedPublicKeys.Count > 0 && ManifestSignature.TrustedPublicKeys.All(k =>
            {
                try { using var e = ECDsa.Create(); e.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k), out _); return e.KeySize == 256; }
                catch (Exception) { return false; }
            }));
    }
}
