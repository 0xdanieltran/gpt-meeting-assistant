using Microsoft.Win32;
using System;
using System.Security.Cryptography;
using System.Text;

namespace PrivateBrowser
{
    public static class DeviceFingerprint
    {
        private const string AppNamespace =
            "PrivateBrowser-License-v1";


        public static string GetDeviceId()
        {
            string machineGuid =
                GetMachineGuid();

            string source =
                machineGuid +
                "|" +
                AppNamespace;

            byte[] sourceBytes =
                Encoding.UTF8.GetBytes(
                    source
                );

            byte[] hash =
                SHA256.HashData(
                    sourceBytes
                );

            string hex =
                Convert.ToHexString(
                    hash
                );

            // Show a shorter, human-readable ID.
            //
            // Example:
            // PBDEV-91A7-4F20-8D61-C3B2
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


        public static string GetFullDeviceHash()
        {
            string machineGuid =
                GetMachineGuid();

            string source =
                machineGuid +
                "|" +
                AppNamespace;

            byte[] sourceBytes =
                Encoding.UTF8.GetBytes(
                    source
                );

            byte[] hash =
                SHA256.HashData(
                    sourceBytes
                );

            return Convert.ToHexString(
                hash
            );
        }


        private static string GetMachineGuid()
        {
            using RegistryKey? key =
                Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography"
                );

            object? value =
                key?.GetValue(
                    "MachineGuid"
                );

            if (value == null)
            {
                throw new InvalidOperationException(
                    "Unable to generate a device ID on this computer."
                );
            }

            string machineGuid =
                value
                    .ToString()?
                    .Trim()
                ??
                string.Empty;

            if (
                string.IsNullOrWhiteSpace(
                    machineGuid
                )
            )
            {
                throw new InvalidOperationException(
                    "Windows MachineGuid is empty."
                );
            }

            return machineGuid;
        }
    }
}