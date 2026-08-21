using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PrivateBrowser
{
    public static class LicenseValidator
    {
        public static bool IsValid(
            string deviceId,
            string licenseKey,
            string publicKeyPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(deviceId)
                ||
                string.IsNullOrWhiteSpace(licenseKey)
            )
            {
                return false;
            }

            if (
                !File.Exists(
                    publicKeyPath
                )
            )
            {
                return false;
            }

            if (
                !licenseKey.StartsWith(
                    "PBL1.",
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            try
            {
                string normalizedDeviceId =
                    deviceId
                        .Trim()
                        .ToUpperInvariant();

                string encodedSignature =
                    licenseKey
                        .Substring(
                            "PBL1.".Length
                        )
                        .Trim();

                if (
                    string.IsNullOrWhiteSpace(
                        encodedSignature
                    )
                )
                {
                    return false;
                }


                // Convert Base64Url back to normal Base64.
                string base64 =
                    encodedSignature
                        .Replace(
                            "-",
                            "+"
                        )
                        .Replace(
                            "_",
                            "/"
                        );

                int padding =
                    base64.Length %
                    4;

                if (padding == 2)
                {
                    base64 +=
                        "==";
                }
                else if (padding == 3)
                {
                    base64 +=
                        "=";
                }
                else if (padding != 0)
                {
                    return false;
                }


                byte[] signature =
                    Convert.FromBase64String(
                        base64
                    );

                byte[] data =
                    Encoding.UTF8.GetBytes(
                        normalizedDeviceId
                    );


                string publicKeyPem =
                    File.ReadAllText(
                        publicKeyPath
                    );


                using ECDsa ecdsa =
                    ECDsa.Create();

                ecdsa.ImportFromPem(
                    publicKeyPem
                );


                return ecdsa.VerifyData(
                    data,
                    signature,
                    HashAlgorithmName.SHA256
                );
            }
            catch
            {
                return false;
            }
        }
    }
}