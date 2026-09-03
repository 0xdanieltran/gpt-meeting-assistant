using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PrivateBrowser
{
    public static class ChromeWebStoreExtensionInstaller
    {
        private static readonly Regex ExtensionIdRegex =
            new Regex(
                @"^[a-p]{32}$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );

        private static readonly Regex StoreIdRegex =
            new Regex(
                @"(?:chromewebstore\.google\.com|chrome\.google\.com/webstore)/detail/(?:[^/]+/)?([a-p]{32})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );

        public static string ExtensionsRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData
                    ),
                    "PrivateBrowser",
                    "Extensions"
                );
            }
        }

        public static bool TryParseExtensionId(
            string input,
            out string extensionId
        )
        {
            extensionId = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed =
                input.Trim();

            Match storeMatch =
                StoreIdRegex.Match(
                    trimmed
                );

            if (storeMatch.Success)
            {
                extensionId =
                    storeMatch.Groups[1].Value.ToLowerInvariant();

                return true;
            }

            if (ExtensionIdRegex.IsMatch(trimmed))
            {
                extensionId =
                    trimmed.ToLowerInvariant();

                return true;
            }

            return false;
        }

        public static async Task<string> DownloadAndUnpackAsync(
            string extensionId,
            CancellationToken cancellationToken = default
        )
        {
            if (!TryParseExtensionId(extensionId, out string id))
            {
                throw new ArgumentException(
                    "Enter a Chrome Web Store link or a 32-character extension ID."
                );
            }

            Directory.CreateDirectory(
                ExtensionsRoot
            );

            string crxPath =
                Path.Combine(
                    ExtensionsRoot,
                    id + ".crx"
                );

            string unpackPath =
                Path.Combine(
                    ExtensionsRoot,
                    id
                );

            string downloadUrl =
                "https://clients2.google.com/service/update2/crx" +
                "?response=redirect" +
                "&prodversion=131.0.6778.86" +
                "&acceptformat=crx2,crx3" +
                "&x=id%3d" + id + "%26uc";

            using HttpClient client =
                new HttpClient();

            client.Timeout =
                TimeSpan.FromMinutes(2);

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            );

            byte[] crxBytes =
                await client.GetByteArrayAsync(
                    downloadUrl,
                    cancellationToken
                );

            if (
                crxBytes.Length < 16 ||
                (
                    crxBytes[0] != (byte)'C' &&
                    crxBytes[0] != (byte)'P'
                )
            )
            {
                throw new InvalidDataException(
                    "Chrome Web Store did not return an extension package. The listing may be unavailable, or this network may block the download."
                );
            }

            await File.WriteAllBytesAsync(
                crxPath,
                crxBytes,
                cancellationToken
            );

            if (Directory.Exists(unpackPath))
            {
                Directory.Delete(
                    unpackPath,
                    true
                );
            }

            Directory.CreateDirectory(
                unpackPath
            );

            ExtractCrx(
                crxPath,
                unpackPath
            );

            string manifestFolder =
                FindManifestFolder(
                    unpackPath
                );

            string? publicKey =
                TryExtractCrxPublicKeyBase64(
                    crxBytes
                );

            if (!string.IsNullOrWhiteSpace(publicKey))
            {
                InjectManifestKey(
                    manifestFolder,
                    publicKey
                );
            }

            try
            {
                File.Delete(
                    crxPath
                );
            }
            catch
            {
            }

            return manifestFolder;
        }


        public static void RememberInstalledPath(
            string extensionId,
            string folderPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(extensionId) ||
                string.IsNullOrWhiteSpace(folderPath)
            )
            {
                return;
            }

            Dictionary<string, string> map =
                LoadInstalledPaths();

            map[extensionId] =
                folderPath;

            SaveInstalledPaths(
                map
            );
        }


        public static string? TryGetLaunchUrl(
            string extensionId
        )
        {
            string? folder =
                TryFindExtensionFolder(
                    extensionId
                );

            string? page =
                TryReadLaunchPage(
                    folder
                );

            if (
                string.IsNullOrWhiteSpace(page)
            )
            {
                page =
                    FindExistingPopupFile(
                        folder
                    );
            }

            if (
                string.IsNullOrWhiteSpace(page) ||
                string.IsNullOrWhiteSpace(folder)
            )
            {
                return null;
            }

            page =
                page.Replace(
                    '\\',
                    '/'
                ).TrimStart(
                    '/'
                );

            string diskFile =
                Path.Combine(
                    folder,
                    page.Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    )
                );

            if (!File.Exists(diskFile))
            {
                page =
                    FindExistingPopupFile(
                        folder
                    );

                if (string.IsNullOrWhiteSpace(page))
                {
                    return null;
                }

                page =
                    page.Replace(
                        '\\',
                        '/'
                    ).TrimStart(
                        '/'
                    );
            }

            RememberInstalledPath(
                extensionId,
                folder
            );

            return $"chrome-extension://{extensionId}/{page}";
        }


        public static string? TryFindExtensionFolder(
            string extensionId
        )
        {
            Dictionary<string, string> map =
                LoadInstalledPaths();

            if (
                map.TryGetValue(
                    extensionId,
                    out string? saved
                )
                &&
                Directory.Exists(
                    saved
                )
            )
            {
                try
                {
                    return FindManifestFolder(
                        saved
                    );
                }
                catch
                {
                }
            }

            string downloaded =
                Path.Combine(
                    ExtensionsRoot,
                    extensionId
                );

            if (Directory.Exists(downloaded))
            {
                try
                {
                    return FindManifestFolder(
                        downloaded
                    );
                }
                catch
                {
                }
            }

            return TryFindSingleUnpackedFolder();
        }


        private static string? TryFindSingleUnpackedFolder()
        {
            if (!Directory.Exists(ExtensionsRoot))
            {
                return null;
            }

            List<string> folders =
                new List<string>();

            foreach (
                string directory
                in Directory.GetDirectories(
                    ExtensionsRoot
                )
            )
            {
                try
                {
                    folders.Add(
                        FindManifestFolder(
                            directory
                        )
                    );
                }
                catch
                {
                }
            }

            if (folders.Count == 1)
            {
                return folders[0];
            }

            return null;
        }


        private static string? FindExistingPopupFile(
            string? folder
        )
        {
            if (
                string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder)
            )
            {
                return null;
            }

            string[] candidates =
            {
                "popup.html",
                "popup/index.html",
                "popup/popup.html",
                "index.html"
            };

            foreach (
                string candidate
                in candidates
            )
            {
                string path =
                    Path.Combine(
                        folder,
                        candidate.Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        )
                    );

                if (File.Exists(path))
                {
                    return candidate;
                }
            }

            return null;
        }


        private static string InstalledPathsFile
        {
            get
            {
                return Path.Combine(
                    ExtensionsRoot,
                    "installed-paths.json"
                );
            }
        }


        private static Dictionary<string, string> LoadInstalledPaths()
        {
            try
            {
                if (!File.Exists(InstalledPathsFile))
                {
                    return new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase
                    );
                }

                string json =
                    File.ReadAllText(
                        InstalledPathsFile
                    );

                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                        json
                    )
                    ??
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase
                    );
            }
            catch
            {
                return new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }


        private static void SaveInstalledPaths(
            Dictionary<string, string> map
        )
        {
            Directory.CreateDirectory(
                ExtensionsRoot
            );

            File.WriteAllText(
                InstalledPathsFile,
                JsonSerializer.Serialize(
                    map
                )
            );
        }


        private static string? TryReadLaunchPage(
            string? folder
        )
        {
            if (
                string.IsNullOrWhiteSpace(folder)
            )
            {
                return null;
            }

            string manifestPath =
                Path.Combine(
                    folder,
                    "manifest.json"
                );

            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        File.ReadAllText(
                            manifestPath
                        )
                    );

                JsonElement root =
                    document.RootElement;

                if (
                    TryGetStringProperty(
                        root,
                        "action",
                        "default_popup",
                        out string? actionPopup
                    )
                )
                {
                    return actionPopup;
                }

                if (
                    TryGetStringProperty(
                        root,
                        "browser_action",
                        "default_popup",
                        out string? browserPopup
                    )
                )
                {
                    return browserPopup;
                }

                if (
                    TryGetStringProperty(
                        root,
                        "page_action",
                        "default_popup",
                        out string? pagePopup
                    )
                )
                {
                    return pagePopup;
                }

                if (
                    TryGetStringProperty(
                        root,
                        "options_ui",
                        "page",
                        out string? optionsUi
                    )
                )
                {
                    return optionsUi;
                }

                if (
                    root.TryGetProperty(
                        "options_page",
                        out JsonElement optionsPage
                    )
                    &&
                    optionsPage.ValueKind ==
                    JsonValueKind.String
                )
                {
                    return optionsPage.GetString();
                }
            }
            catch
            {
            }

            return null;
        }


        private static bool TryGetStringProperty(
            JsonElement root,
            string parentName,
            string childName,
            out string? value
        )
        {
            value =
                null;

            if (
                !root.TryGetProperty(
                    parentName,
                    out JsonElement parent
                )
                ||
                parent.ValueKind !=
                JsonValueKind.Object
            )
            {
                return false;
            }

            if (
                !parent.TryGetProperty(
                    childName,
                    out JsonElement child
                )
                ||
                child.ValueKind !=
                JsonValueKind.String
            )
            {
                return false;
            }

            value =
                child.GetString();

            return !string.IsNullOrWhiteSpace(
                value
            );
        }

        public static string FindManifestFolder(
            string rootPath
        )
        {
            string direct =
                Path.Combine(
                    rootPath,
                    "manifest.json"
                );

            if (File.Exists(direct))
            {
                return rootPath;
            }

            foreach (
                string directory
                in Directory.GetDirectories(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                if (
                    File.Exists(
                        Path.Combine(
                            directory,
                            "manifest.json"
                        )
                    )
                )
                {
                    return directory;
                }
            }

            throw new FileNotFoundException(
                "The extension folder does not contain a manifest.json file.",
                rootPath
            );
        }

        private static void ExtractCrx(
            string crxPath,
            string destination
        )
        {
            byte[] data =
                File.ReadAllBytes(
                    crxPath
                );

            int zipOffset =
                IndexOfZipHeader(
                    data
                );

            if (zipOffset < 0)
            {
                throw new InvalidDataException(
                    "The downloaded file is not a valid Chrome extension package."
                );
            }

            string zipPath =
                crxPath + ".zip";

            File.WriteAllBytes(
                zipPath,
                data[zipOffset..]
            );

            try
            {
                ZipFile.ExtractToDirectory(
                    zipPath,
                    destination,
                    true
                );
            }
            finally
            {
                try
                {
                    File.Delete(
                        zipPath
                    );
                }
                catch
                {
                }
            }
        }

        private static int IndexOfZipHeader(
            byte[] data
        )
        {
            for (
                int i = 0;
                i <= data.Length - 4;
                i++
            )
            {
                if (
                    data[i] == 0x50 &&
                    data[i + 1] == 0x4B &&
                    data[i + 2] == 0x03 &&
                    data[i + 3] == 0x04
                )
                {
                    return i;
                }
            }

            return -1;
        }


        private static string? TryExtractCrxPublicKeyBase64(
            byte[] crx
        )
        {
            if (
                crx.Length < 16 ||
                crx[0] != (byte)'C' ||
                crx[1] != (byte)'r' ||
                crx[2] != (byte)'2' ||
                crx[3] != (byte)'4'
            )
            {
                return null;
            }

            int version =
                BitConverter.ToInt32(
                    crx,
                    4
                );

            if (version != 3)
            {
                return null;
            }

            int headerSize =
                BitConverter.ToInt32(
                    crx,
                    8
                );

            if (
                headerSize <= 0 ||
                12 + headerSize >
                crx.Length
            )
            {
                return null;
            }

            byte[] header =
                crx[12..(12 + headerSize)];

            int offset =
                0;

            while (offset < header.Length)
            {
                if (
                    !TryReadProtobufKey(
                        header,
                        ref offset,
                        out int field,
                        out int wire
                    )
                )
                {
                    break;
                }

                if (wire != 2)
                {
                    if (
                        !TrySkipProtobufValue(
                            header,
                            ref offset,
                            wire
                        )
                    )
                    {
                        break;
                    }

                    continue;
                }

                if (
                    !TryReadVarint(
                        header,
                        ref offset,
                        out int length
                    )
                    ||
                    offset + length >
                    header.Length
                )
                {
                    break;
                }

                if (field == 2)
                {
                    byte[] proof =
                        header[offset..(offset + length)];

                    byte[]? publicKey =
                        TryReadNestedPublicKey(
                            proof
                        );

                    if (publicKey != null && publicKey.Length > 0)
                    {
                        return Convert.ToBase64String(
                            publicKey
                        );
                    }
                }

                offset +=
                    length;
            }

            return null;
        }


        private static byte[]? TryReadNestedPublicKey(
            byte[] proof
        )
        {
            int offset =
                0;

            while (offset < proof.Length)
            {
                if (
                    !TryReadProtobufKey(
                        proof,
                        ref offset,
                        out int field,
                        out int wire
                    )
                )
                {
                    break;
                }

                if (wire != 2)
                {
                    if (
                        !TrySkipProtobufValue(
                            proof,
                            ref offset,
                            wire
                        )
                    )
                    {
                        break;
                    }

                    continue;
                }

                if (
                    !TryReadVarint(
                        proof,
                        ref offset,
                        out int length
                    )
                    ||
                    offset + length >
                    proof.Length
                )
                {
                    break;
                }

                if (field == 1)
                {
                    return proof[offset..(offset + length)];
                }

                offset +=
                    length;
            }

            return null;
        }


        private static bool TryReadProtobufKey(
            byte[] data,
            ref int offset,
            out int field,
            out int wire
        )
        {
            field =
                0;

            wire =
                0;

            if (
                !TryReadVarint(
                    data,
                    ref offset,
                    out int key
                )
            )
            {
                return false;
            }

            field =
                key >> 3;

            wire =
                key & 7;

            return true;
        }


        private static bool TryReadVarint(
            byte[] data,
            ref int offset,
            out int value
        )
        {
            value =
                0;

            int shift =
                0;

            while (offset < data.Length)
            {
                byte current =
                    data[offset++];

                value |=
                    (current & 0x7F) <<
                    shift;

                if ((current & 0x80) == 0)
                {
                    return true;
                }

                shift +=
                    7;

                if (shift > 28)
                {
                    return false;
                }
            }

            return false;
        }


        private static bool TrySkipProtobufValue(
            byte[] data,
            ref int offset,
            int wire
        )
        {
            switch (wire)
            {
                case 0:
                    return TryReadVarint(
                        data,
                        ref offset,
                        out _
                    );

                case 1:
                    if (offset + 8 > data.Length)
                    {
                        return false;
                    }

                    offset +=
                        8;

                    return true;

                case 2:
                    if (
                        !TryReadVarint(
                            data,
                            ref offset,
                            out int length
                        )
                        ||
                        offset + length >
                        data.Length
                    )
                    {
                        return false;
                    }

                    offset +=
                        length;

                    return true;

                case 5:
                    if (offset + 4 > data.Length)
                    {
                        return false;
                    }

                    offset +=
                        4;

                    return true;

                default:
                    return false;
            }
        }


        private static void InjectManifestKey(
            string folder,
            string publicKey
        )
        {
            string manifestPath =
                Path.Combine(
                    folder,
                    "manifest.json"
                );

            if (!File.Exists(manifestPath))
            {
                return;
            }

            JsonNode? node =
                JsonNode.Parse(
                    File.ReadAllText(
                        manifestPath
                    )
                );

            if (node is not JsonObject obj)
            {
                return;
            }

            obj["key"] =
                publicKey;

            File.WriteAllText(
                manifestPath,
                obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }
                )
            );
        }
    }
}
