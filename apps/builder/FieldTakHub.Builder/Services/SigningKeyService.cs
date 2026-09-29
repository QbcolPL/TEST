using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace FieldTakHub.Builder.Services;

public sealed class SigningKeyService
{
    private readonly string _keyPath;
    private readonly SignatureAlgorithm _algorithm = SignatureAlgorithm.Ed25519;

    public SigningKeyService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FieldTakHub", "keys");
        Directory.CreateDirectory(dir);
        _keyPath = Path.Combine(dir, "default.ed25519.dpapi");
    }

    public (byte[] Signature, byte[] PublicKey, string Fingerprint) Sign(ReadOnlySpan<byte> data)
    {
        using var key = LoadOrCreate();
        var sig = _algorithm.Sign(key, data);
        var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var fp = Convert.ToHexString(SHA256.HashData(pub)).ToLowerInvariant();
        return (sig, pub, fp);
    }

    public string Fingerprint()
    {
        using var key=LoadOrCreate();
        return Convert.ToHexString(SHA256.HashData(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant();
    }

    public void ExportEncryptedBackup(string path, string password)
    {
        if (password.Length < 10) throw new InvalidDataException("FTH-KEY-001: backup password must contain at least 10 characters.");
        using (LoadOrCreate()) { } // ensure a local key exists
        var raw=ProtectedData.Unprotect(File.ReadAllBytes(_keyPath),null,DataProtectionScope.CurrentUser);
        using var key=Key.Import(_algorithm,raw,KeyBlobFormat.RawPrivateKey);
        var salt=RandomNumberGenerator.GetBytes(16); var nonce=RandomNumberGenerator.GetBytes(12); var tag=new byte[16]; var cipher=new byte[raw.Length];
        var derived=Rfc2898DeriveBytes.Pbkdf2(password,salt,600_000,HashAlgorithmName.SHA256,32);
        try
        {
            using var aes=new AesGcm(derived,16);
            aes.Encrypt(nonce,raw,cipher,tag,"FieldTAKHub.fthkey.v1"u8.ToArray());
            var doc=new {
                schema="fieldtak.publisher-key-backup", version=1, kdf="PBKDF2-SHA256", iterations=600000, cipher="AES-256-GCM",
                fingerprint=Convert.ToHexString(SHA256.HashData(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant(),
                salt=Convert.ToBase64String(salt), nonce=Convert.ToBase64String(nonce), tag=Convert.ToBase64String(tag), ciphertext=Convert.ToBase64String(cipher)
            };
            File.WriteAllText(path,JsonSerializer.Serialize(doc,JsonDefaults.Options));
        }
        finally { CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(derived); }
    }

    public string ImportEncryptedBackup(string path, string password)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path)); var root=doc.RootElement;
        if(root.GetProperty("schema").GetString()!="fieldtak.publisher-key-backup" || root.GetProperty("version").GetInt32()!=1)
            throw new InvalidDataException("FTH-KEY-002: unsupported publisher key backup.");
        var iterations=root.GetProperty("iterations").GetInt32();
        var salt=Convert.FromBase64String(root.GetProperty("salt").GetString()!); var nonce=Convert.FromBase64String(root.GetProperty("nonce").GetString()!);
        var tag=Convert.FromBase64String(root.GetProperty("tag").GetString()!); var cipher=Convert.FromBase64String(root.GetProperty("ciphertext").GetString()!);
        var raw=new byte[cipher.Length]; var derived=Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,32);
        try
        {
            using var aes=new AesGcm(derived,16); aes.Decrypt(nonce,cipher,tag,raw,"FieldTAKHub.fthkey.v1"u8.ToArray());
            using var imported=Key.Import(_algorithm,raw,KeyBlobFormat.RawPrivateKey);
            var fingerprint=Convert.ToHexString(SHA256.HashData(imported.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant();
            var expected=root.GetProperty("fingerprint").GetString();
            if(!fingerprint.Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("FTH-KEY-003: backup fingerprint mismatch.");
            var protectedBlob=ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_keyPath,protectedBlob);
            return fingerprint;
        }
        catch(CryptographicException ex) when(!ex.Message.StartsWith("FTH-KEY-",StringComparison.Ordinal)) { throw new CryptographicException("FTH-KEY-004: wrong password or damaged backup.",ex); }
        finally { CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(derived); }
    }

    private Key LoadOrCreate()
    {
        if (File.Exists(_keyPath))
        {
            var protectedBlob = File.ReadAllBytes(_keyPath);
            var raw = ProtectedData.Unprotect(protectedBlob, null, DataProtectionScope.CurrentUser);
            try { return Key.Import(_algorithm, raw, KeyBlobFormat.RawPrivateKey); }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }

        using var created = Key.Create(_algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var rawPrivate = created.Export(KeyBlobFormat.RawPrivateKey);
        try
        {
            var protectedBlob = ProtectedData.Protect(rawPrivate, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_keyPath, protectedBlob);
        }
        finally { CryptographicOperations.ZeroMemory(rawPrivate); }
        var importedRaw = ProtectedData.Unprotect(File.ReadAllBytes(_keyPath), null, DataProtectionScope.CurrentUser);
        try { return Key.Import(_algorithm, importedRaw, KeyBlobFormat.RawPrivateKey); }
        finally { CryptographicOperations.ZeroMemory(importedRaw); }
    }
}
