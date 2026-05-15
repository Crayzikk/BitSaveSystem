using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// AES-256-CBC з HMAC-SHA256 (Encrypt-then-MAC).
    /// CBC обрано замість GCM, бо AesGcm кидає PlatformNotSupportedException
    /// на старіших версіях Unity і деяких mobile-таргетах. CBC + HMAC — безпечно
    /// і працює скрізь.
    ///
    /// Структура файлу: [hmac:32][iv:16][ciphertext:N]
    /// </summary>
    public sealed class EncryptionService
    {
        private const string EncKeyPref = "SS_EncKey_v3";
        private const string MacKeyPref = "SS_MacKey_v3";
        private const int KeySize = 32; // 256 біт
        private const int IvSize  = 16;
        private const int MacSize = 32;

        private readonly byte[] _encKey;
        private readonly byte[] _macKey;

        public EncryptionService()
        {
            _encKey = LoadOrCreateKey(EncKeyPref);
            _macKey = LoadOrCreateKey(MacKeyPref);
        }

        /// <summary>Опційно: дозволяє кодеру передати власні ключі (наприклад, з серверу).</summary>
        public EncryptionService(byte[] encKey, byte[] macKey)
        {
            if (encKey == null || encKey.Length != KeySize) throw new ArgumentException("encKey must be 32 bytes");
            if (macKey == null || macKey.Length != KeySize) throw new ArgumentException("macKey must be 32 bytes");
            _encKey = encKey;
            _macKey = macKey;
        }

        private static byte[] LoadOrCreateKey(string pref)
        {
            string b64 = PlayerPrefs.GetString(pref, "");
            if (!string.IsNullOrEmpty(b64))
            {
                try { return Convert.FromBase64String(b64); } catch { /* re-create */ }
            }
            byte[] key = new byte[KeySize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(key);
            PlayerPrefs.SetString(pref, Convert.ToBase64String(key));
            PlayerPrefs.Save();
            return key;
        }

        public byte[] Encrypt(byte[] plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = _encKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            byte[] iv = aes.IV;

            using var enc = aes.CreateEncryptor();
            byte[] ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);

            // HMAC покриває IV + ciphertext (Encrypt-then-MAC)
            using var hmac = new HMACSHA256(_macKey);
            byte[] toMac = new byte[iv.Length + ciphertext.Length];
            Buffer.BlockCopy(iv, 0, toMac, 0, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, toMac, iv.Length, ciphertext.Length);
            byte[] mac = hmac.ComputeHash(toMac);

            byte[] result = new byte[MacSize + IvSize + ciphertext.Length];
            Buffer.BlockCopy(mac,        0, result, 0,                   MacSize);
            Buffer.BlockCopy(iv,         0, result, MacSize,             IvSize);
            Buffer.BlockCopy(ciphertext, 0, result, MacSize + IvSize,    ciphertext.Length);
            return result;
        }

        public byte[] Decrypt(byte[] data)
        {
            if (data == null || data.Length < MacSize + IvSize)
                throw new CryptographicException("Encrypted data too short.");

            byte[] mac = new byte[MacSize];
            byte[] iv  = new byte[IvSize];
            byte[] ciphertext = new byte[data.Length - MacSize - IvSize];

            Buffer.BlockCopy(data, 0,                   mac,        0, MacSize);
            Buffer.BlockCopy(data, MacSize,             iv,         0, IvSize);
            Buffer.BlockCopy(data, MacSize + IvSize,    ciphertext, 0, ciphertext.Length);

            // 1) Спершу перевіряємо HMAC — якщо не збігається, не розшифровуємо взагалі
            using var hmac = new HMACSHA256(_macKey);
            byte[] toMac = new byte[iv.Length + ciphertext.Length];
            Buffer.BlockCopy(iv,         0, toMac, 0,          iv.Length);
            Buffer.BlockCopy(ciphertext, 0, toMac, iv.Length,  ciphertext.Length);
            byte[] expectedMac = hmac.ComputeHash(toMac);

            if (!ConstantTimeEquals(mac, expectedMac))
                throw new CryptographicException("HMAC mismatch — file is tampered or corrupt.");

            // 2) Розшифрування
            using var aes = Aes.Create();
            aes.Key = _encKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        /// <summary>Constant-time comparison — захист від timing attacks.</summary>
        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
