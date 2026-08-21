using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PrivateBrowser
{
    public static class LicenseStorage
    {
        private static readonly string LicenseDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
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
            if (
                string.IsNullOrWhiteSpace(
                    licenseKey
                )
            )
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
                ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

            File.WriteAllBytes(
                LicenseFilePath,
                protectedBytes
            );
        }


        public static string? Load()
        {
            if (
                !File.Exists(
                    LicenseFilePath
                )
            )
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
                    ProtectedData.Unprotect(
                        protectedBytes,
                        null,
                        DataProtectionScope.CurrentUser
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
            if (
                File.Exists(
                    LicenseFilePath
                )
            )
            {
                File.Delete(
                    LicenseFilePath
                );
            }
        }
    }
}