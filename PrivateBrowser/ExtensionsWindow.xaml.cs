using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;

namespace PrivateBrowser
{
    public partial class ExtensionsWindow : Window
    {
        private readonly CoreWebView2 _webView;
        private readonly Action<string> _navigate;

        public ExtensionsWindow(
            CoreWebView2 webView,
            Action<string> navigate
        )
        {
            _webView =
                webView;

            _navigate =
                navigate;

            InitializeComponent();

            Loaded +=
                ExtensionsWindow_Loaded;
        }

        private async void ExtensionsWindow_Loaded(
            object sender,
            RoutedEventArgs e
        )
        {
            InstallPathText.Text =
                "Install folder: " +
                ChromeWebStoreExtensionInstaller.ExtensionsRoot;

            await RefreshExtensionsAsync();
        }

        private async void InstallButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            await InstallFromStoreInputAsync();
        }

        private async void StoreInput_KeyDown(
            object sender,
            WpfKeyEventArgs e
        )
        {
            if (e.Key == WpfKey.Enter)
            {
                await InstallFromStoreInputAsync();
            }
        }

        private async Task InstallFromStoreInputAsync()
        {
            string input =
                StoreInput.Text;

            if (
                !ChromeWebStoreExtensionInstaller.TryParseExtensionId(
                    input,
                    out string extensionId
                )
            )
            {
                WpfMessageBox.Show(
                    this,
                    "Paste a Chrome Web Store link or a 32-character extension ID.",
                    "Extensions",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }

            await InstallFromFolderAsync(
                async () =>
                {
                    SetBusy(
                        true,
                        "Downloading extension..."
                    );

                    return await ChromeWebStoreExtensionInstaller
                        .DownloadAndUnpackAsync(
                            extensionId
                        );
                }
            );
        }

        private async void LoadUnpackedButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            OpenFolderDialog dialog =
                new OpenFolderDialog
                {
                    Title =
                        "Select the unpacked extension folder"
                };

            bool? confirmed;

            Window? owner =
                Owner;

            bool ownerWasTopmost =
                owner?.Topmost ?? false;

            try
            {
                if (owner != null)
                {
                    owner.Topmost =
                        false;
                }

                Topmost =
                    false;

                confirmed =
                    dialog.ShowDialog(
                        this
                    );
            }
            finally
            {
                if (owner != null)
                {
                    owner.Topmost =
                        ownerWasTopmost;
                }

                BringToFrontOverOwner();
            }

            if (confirmed != true)
            {
                return;
            }

            await InstallFromFolderAsync(
                () =>
                {
                    return Task.FromResult(
                        ChromeWebStoreExtensionInstaller.FindManifestFolder(
                            dialog.FolderName
                        )
                    );
                }
            );

            BringToFrontOverOwner();
        }

        private async void OpenButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            OpenSelectedExtension();
        }

        private void ExtensionsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e
        )
        {
            OpenSelectedExtension();
        }

        private void OpenSelectedExtension()
        {
            if (
                ExtensionsList.SelectedItem is not ExtensionListItem selected
            )
            {
                WpfMessageBox.Show(
                    this,
                    "Select an extension first.",
                    "Extensions",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }

            string? url =
                ChromeWebStoreExtensionInstaller.TryGetLaunchUrl(
                    selected.Extension.Id
                );

            if (string.IsNullOrWhiteSpace(url))
            {
                WpfMessageBox.Show(
                    this,
                    "This extension does not expose a popup or options page to open.",
                    "Extensions",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }

            _navigate(
                url
            );

            DialogResult =
                true;
        }

        private async void RemoveButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (
                ExtensionsList.SelectedItem is not ExtensionListItem selected
            )
            {
                return;
            }

            try
            {
                SetBusy(
                    true,
                    "Removing extension..."
                );

                await selected.Extension.RemoveAsync();

                await RefreshExtensionsAsync();

                StatusText.Text =
                    $"Removed {selected.Extension.Name}.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    string.Empty;

                WpfMessageBox.Show(
                    this,
                    $"Could not remove the extension.\n\n{ex.Message}",
                    "Extensions",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning
                );
            }
            finally
            {
                SetBusy(
                    false,
                    StatusText.Text
                );
            }
        }

        private async Task InstallFromFolderAsync(
            Func<Task<string>> resolveFolder
        )
        {
            try
            {
                SetBusy(
                    true,
                    "Installing extension..."
                );

                string folder =
                    await resolveFolder();

                CoreWebView2BrowserExtension extension =
                    await _webView.Profile.AddBrowserExtensionAsync(
                        folder
                    );

                ChromeWebStoreExtensionInstaller.RememberInstalledPath(
                    extension.Id,
                    folder
                );

                await RefreshExtensionsAsync();

                StatusText.Text =
                    $"Installed {extension.Name}. Select it and click Open, or double-click it.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    string.Empty;

                WpfMessageBox.Show(
                    this,
                    $"Could not install the extension.\n\n{ex.Message}",
                    "Extensions",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning
                );
            }
            finally
            {
                SetBusy(
                    false,
                    StatusText.Text
                );

                BringToFrontOverOwner();
            }
        }

        private async Task RefreshExtensionsAsync()
        {
            IReadOnlyList<CoreWebView2BrowserExtension> extensions =
                await _webView.Profile.GetBrowserExtensionsAsync();

            List<ExtensionListItem> items =
                new List<ExtensionListItem>();

            foreach (
                CoreWebView2BrowserExtension extension
                in extensions
            )
            {
                items.Add(
                    new ExtensionListItem(
                        extension
                    )
                );
            }

            ExtensionsList.ItemsSource =
                items;
        }

        private void SetBusy(
            bool busy,
            string? status
        )
        {
            InstallButton.IsEnabled =
                !busy;

            LoadUnpackedButton.IsEnabled =
                !busy;

            OpenButton.IsEnabled =
                !busy;

            RemoveButton.IsEnabled =
                !busy;

            StoreInput.IsEnabled =
                !busy;

            StatusText.Text =
                status ?? string.Empty;
        }

        private void BringToFrontOverOwner()
        {
            Topmost =
                false;

            Topmost =
                true;

            Activate();
            Focus();
        }

        private sealed class ExtensionListItem
        {
            public ExtensionListItem(
                CoreWebView2BrowserExtension extension
            )
            {
                Extension =
                    extension;
            }

            public CoreWebView2BrowserExtension Extension
            {
                get;
            }

            public override string ToString()
            {
                string state =
                    Extension.IsEnabled
                        ? "enabled"
                        : "disabled";

                return $"{Extension.Name} ({state})";
            }
        }
    }
}
