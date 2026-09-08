using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PrivateBrowser.Mac
{
    public static class DeviceFingerprint
    {
        private const string AppNamespace =
            "PrivateBrowser-License-v1";

        public static string GetDeviceId()
        {
            string hex =
                Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            GetMachineGuid() + "|" + AppNamespace
                        )
                    )
                );

            return
                "PBDEV-" +
                hex.Substring(0, 4) +
                "-" +
                hex.Substring(4, 4) +
                "-" +
                hex.Substring(8, 4) +
                "-" +
                hex.Substring(12, 4);
        }

        public static string GetMachineGuid()
        {
            if (OperatingSystem.IsMacOS())
            {
                string? uuid =
                    TryReadMacPlatformUuid();

                if (!string.IsNullOrWhiteSpace(uuid))
                {
                    return uuid.Trim();
                }
            }

            string fallback =
                Environment.MachineName + "|" +
                Environment.UserName + "|" +
                Environment.OSVersion;

            return fallback;
        }

        private static string? TryReadMacPlatformUuid()
        {
            try
            {
                using Process process =
                    new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/usr/sbin/ioreg",
                            Arguments = "-rd1 -c IOPlatformExpertDevice",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                process.Start();
                string output =
                    process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                const string marker =
                    "\"IOPlatformUUID\"";

                int index =
                    output.IndexOf(
                        marker,
                        StringComparison.Ordinal
                    );

                if (index < 0)
                {
                    return null;
                }

                int start =
                    output.IndexOf(
                        '"',
                        index + marker.Length
                    );

                int end =
                    start >= 0
                        ? output.IndexOf(
                            '"',
                            start + 1
                        )
                        : -1;

                if (start < 0 || end < 0)
                {
                    return null;
                }

                return output.Substring(
                    start + 1,
                    end - start - 1
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
