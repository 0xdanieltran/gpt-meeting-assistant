using System.Security.Cryptography;
using System.Text;

namespace PrivateBrowser.Mac
{
    public static class LicenseStorage
    {
        private static readonly string LicenseDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData
                ),
                "PrivateBrowser"
            );

        private static readonly string LicenseFilePath =
            Path.Combine(
                LicenseDirectory,
                "license.dat"
            );

        public static void Save(
            string licenseKey
        )
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                throw new ArgumentException(
                    "License key is required.",
                    nameof(licenseKey)
                );
            }

            Directory.CreateDirectory(
                LicenseDirectory
            );

            byte[] plainBytes =
                Encoding.UTF8.GetBytes(
                    licenseKey.Trim()
                );

            byte[] protectedBytes =
                Protect(
                    plainBytes
                );

            File.WriteAllBytes(
                LicenseFilePath,
                protectedBytes
            );
        }

        public static string? Load()
        {
            if (!File.Exists(LicenseFilePath))
            {
                return null;
            }

            try
            {
                byte[] protectedBytes =
                    File.ReadAllBytes(
                        LicenseFilePath
                    );

                byte[] plainBytes =
                    Unprotect(
                        protectedBytes
                    );

                return Encoding.UTF8.GetString(
                    plainBytes
                );
            }
            catch
            {
                return null;
            }
        }

        public static void Delete()
        {
            if (File.Exists(LicenseFilePath))
            {
                File.Delete(
                    LicenseFilePath
                );
            }
        }

        private static byte[] Protect(
            byte[] plainBytes
        )
        {
            byte[] key =
                DeriveKey();

            byte[] nonce =
                RandomNumberGenerator.GetBytes(
                    12
                );

            byte[] cipher =
                new byte[plainBytes.Length];

            byte[] tag =
                new byte[16];

            using AesGcm aes =
                new AesGcm(
                    key,
                    16
                );

            aes.Encrypt(
                nonce,
                plainBytes,
                cipher,
                tag
            );

            byte[] output =
                new byte[nonce.Length + tag.Length + cipher.Length];

            Buffer.BlockCopy(
                nonce,
                0,
                output,
                0,
                nonce.Length
            );

            Buffer.BlockCopy(
                tag,
                0,
                output,
                nonce.Length,
                tag.Length
            );

            Buffer.BlockCopy(
                cipher,
                0,
                output,
                nonce.Length + tag.Length,
                cipher.Length
            );

            return output;
        }

        private static byte[] Unprotect(
            byte[] protectedBytes
        )
        {
            if (protectedBytes.Length < 28)
            {
                throw new CryptographicException(
                    "License file is corrupt."
                );
            }

            byte[] key =
                DeriveKey();

            byte[] nonce =
                protectedBytes[0..12];

            byte[] tag =
                protectedBytes[12..28];

            byte[] cipher =
                protectedBytes[28..];

            byte[] plain =
                new byte[cipher.Length];

            using AesGcm aes =
                new AesGcm(
                    key,
                    16
                );

            aes.Decrypt(
                nonce,
                cipher,
                tag,
                plain
            );

            return plain;
        }

        private static byte[] DeriveKey()
        {
            byte[] salt =
                Encoding.UTF8.GetBytes(
                    "PrivateBrowser.Mac.License.v1"
                );

            return Rfc2898DeriveBytes.Pbkdf2(
                DeviceFingerprint.GetMachineGuid(),
                salt,
                100000,
                HashAlgorithmName.SHA256,
                32
            );
        }
    }
}
