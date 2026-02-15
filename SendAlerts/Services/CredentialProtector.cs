using System;
using System.IO;
using System.Security.Cryptography;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// Encrypts/decrypts sensitive credential fields using AES-CBC + HMAC-SHA256.
/// Key is stored in a local file alongside settings.json.
/// Encrypted values are prefixed with "ENC:" for detection.
/// </summary>
public static class CredentialProtector
{
    private const string EncryptedPrefix = "ENC:";
    private const string KeyFileName = ".credentials.key";
    private const int KeySize = 32; // 256-bit AES key
    private const int HmacKeySize = 32; // 256-bit HMAC key
    private const int IvSize = 16; // AES block size

    private static byte[]? _cachedAesKey;
    private static byte[]? _cachedHmacKey;
    private static readonly object _keyLock = new();

    /// <summary>
    /// Check if a value is already encrypted (has ENC: prefix)
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        return value != null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Encrypt a plaintext value. Returns "ENC:base64data" format.
    /// Returns the original value if null/empty.
    /// </summary>
    public static string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        if (IsEncrypted(plainText)) return plainText; // Already encrypted

        try
        {
            var (aesKey, hmacKey) = LoadOrCreateKeys();

            // Generate random IV
            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            // Encrypt with AES-CBC
            byte[] cipherText;
            using (var aes = Aes.Create())
            {
                aes.Key = aesKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            // Compute HMAC-SHA256 over IV + cipherText
            byte[] hmac;
            using (var hmacAlg = new HMACSHA256(hmacKey))
            {
                var dataToMac = new byte[iv.Length + cipherText.Length];
                Buffer.BlockCopy(iv, 0, dataToMac, 0, iv.Length);
                Buffer.BlockCopy(cipherText, 0, dataToMac, iv.Length, cipherText.Length);
                hmac = hmacAlg.ComputeHash(dataToMac);
            }

            // Format: IV (16) + CipherText (N) + HMAC (32)
            var result = new byte[iv.Length + cipherText.Length + hmac.Length];
            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, result, iv.Length, cipherText.Length);
            Buffer.BlockCopy(hmac, 0, result, iv.Length + cipherText.Length, hmac.Length);

            return EncryptedPrefix + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CredentialProtector] Failed to encrypt value");
            return plainText; // Return original on failure to avoid data loss
        }
    }

    /// <summary>
    /// Decrypt an encrypted value. Detects "ENC:" prefix.
    /// Returns plaintext values as-is (backward compatibility).
    /// </summary>
    public static string? Decrypt(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return encryptedText;
        if (!IsEncrypted(encryptedText)) return encryptedText; // Not encrypted, return as-is

        try
        {
            var (aesKey, hmacKey) = LoadOrCreateKeys();

            var data = Convert.FromBase64String(encryptedText[EncryptedPrefix.Length..]);

            // Minimum size: IV (16) + at least 1 block (16) + HMAC (32) = 64
            if (data.Length < IvSize + 16 + 32)
            {
                Log.Warning("[CredentialProtector] Encrypted data too short, returning empty");
                return string.Empty;
            }

            // Extract parts
            var iv = new byte[IvSize];
            var hmacLength = 32;
            var cipherTextLength = data.Length - IvSize - hmacLength;
            var cipherText = new byte[cipherTextLength];
            var storedHmac = new byte[hmacLength];

            Buffer.BlockCopy(data, 0, iv, 0, IvSize);
            Buffer.BlockCopy(data, IvSize, cipherText, 0, cipherTextLength);
            Buffer.BlockCopy(data, IvSize + cipherTextLength, storedHmac, 0, hmacLength);

            // Verify HMAC
            byte[] computedHmac;
            using (var hmacAlg = new HMACSHA256(hmacKey))
            {
                var dataToMac = new byte[IvSize + cipherTextLength];
                Buffer.BlockCopy(iv, 0, dataToMac, 0, IvSize);
                Buffer.BlockCopy(cipherText, 0, dataToMac, IvSize, cipherTextLength);
                computedHmac = hmacAlg.ComputeHash(dataToMac);
            }

            if (!CryptographicOperations.FixedTimeEquals(storedHmac, computedHmac))
            {
                Log.Warning("[CredentialProtector] HMAC verification failed - data may have been tampered with");
                return string.Empty;
            }

            // Decrypt
            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CredentialProtector] Failed to decrypt value");
            return string.Empty;
        }
    }

    /// <summary>
    /// Protect all sensitive fields in settings before saving
    /// </summary>
    public static void ProtectSensitiveFields(AppSettings settings)
    {
        // AppSettings legacy fields
        settings.TelegramBotToken = Encrypt(settings.TelegramBotToken) ?? string.Empty;
        settings.LineNotifyAccessToken = Encrypt(settings.LineNotifyAccessToken) ?? string.Empty;
        settings.HttpApiKey = Encrypt(settings.HttpApiKey) ?? string.Empty;

        // AlertActionConfig sensitive fields
        foreach (var action in settings.AlertActions)
        {
            action.TelegramBotToken = Encrypt(action.TelegramBotToken);
            action.DiscordWebhookUrl = Encrypt(action.DiscordWebhookUrl);
            action.SmtpPassword = Encrypt(action.SmtpPassword);
            action.WebhookUrl = Encrypt(action.WebhookUrl);
        }
    }

    /// <summary>
    /// Unprotect all sensitive fields in settings after loading
    /// </summary>
    public static void UnprotectSensitiveFields(AppSettings settings)
    {
        // AppSettings legacy fields
        settings.TelegramBotToken = Decrypt(settings.TelegramBotToken) ?? string.Empty;
        settings.LineNotifyAccessToken = Decrypt(settings.LineNotifyAccessToken) ?? string.Empty;
        settings.HttpApiKey = Decrypt(settings.HttpApiKey) ?? string.Empty;

        // AlertActionConfig sensitive fields
        foreach (var action in settings.AlertActions)
        {
            action.TelegramBotToken = Decrypt(action.TelegramBotToken);
            action.DiscordWebhookUrl = Decrypt(action.DiscordWebhookUrl);
            action.SmtpPassword = Decrypt(action.SmtpPassword);
            action.WebhookUrl = Decrypt(action.WebhookUrl);
        }
    }

    private static (byte[] aesKey, byte[] hmacKey) LoadOrCreateKeys()
    {
        lock (_keyLock)
        {
            if (_cachedAesKey != null && _cachedHmacKey != null)
                return (_cachedAesKey, _cachedHmacKey);

            var keyFilePath = GetKeyFilePath();

            if (File.Exists(keyFilePath))
            {
                var keyData = File.ReadAllBytes(keyFilePath);
                if (keyData.Length == KeySize + HmacKeySize)
                {
                    _cachedAesKey = keyData[..KeySize];
                    _cachedHmacKey = keyData[KeySize..];
                    Log.Debug("[CredentialProtector] Loaded encryption keys from {Path}", keyFilePath);
                    return (_cachedAesKey, _cachedHmacKey);
                }

                Log.Warning("[CredentialProtector] Key file has invalid size, regenerating");
            }

            // Generate new keys
            _cachedAesKey = new byte[KeySize];
            _cachedHmacKey = new byte[HmacKeySize];
            RandomNumberGenerator.Fill(_cachedAesKey);
            RandomNumberGenerator.Fill(_cachedHmacKey);

            // Save combined key file
            var combinedKey = new byte[KeySize + HmacKeySize];
            Buffer.BlockCopy(_cachedAesKey, 0, combinedKey, 0, KeySize);
            Buffer.BlockCopy(_cachedHmacKey, 0, combinedKey, KeySize, HmacKeySize);

            var directory = Path.GetDirectoryName(keyFilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(keyFilePath, combinedKey);

            Log.Information("[CredentialProtector] Generated new encryption keys at {Path}", keyFilePath);
            return (_cachedAesKey, _cachedHmacKey);
        }
    }

    private static string GetKeyFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, "SendAlerts", KeyFileName);
    }
}
