using System;
using System.IO;
using System.Windows;

using WpfClipboard =
    System.Windows.Clipboard;

using WpfMessageBox =
    System.Windows.MessageBox;

using WpfMessageBoxButton =
    System.Windows.MessageBoxButton;

using WpfMessageBoxImage =
    System.Windows.MessageBoxImage;


namespace PrivateBrowser
{
    public partial class ActivationWindow : Window
    {
        private readonly string _deviceId;

        private readonly string _publicKeyPath;


        public ActivationWindow()
        {
            InitializeComponent();


            _deviceId =
                DeviceFingerprint.GetDeviceId();


            _publicKeyPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Keys",
                    "public-key.pem"
                );


            DeviceIdTextBox.Text =
                _deviceId;
        }


        private async void CopyDeviceIdButton_Click(
    object sender,
    RoutedEventArgs e
)
        {
            if (
                string.IsNullOrWhiteSpace(
                    _deviceId
                )
            )
            {
                return;
            }

            bool copied =
                await TrySetClipboardTextAsync(
                    _deviceId
                );

            if (copied)
            {
                CopyDeviceIdButton.Content =
                    "Copied";

                return;
            }

            WpfMessageBox.Show(
                "Could not copy Device ID. Please try again.",
                "PrivateBrowser",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning
            );
        }


        private void ActivateButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            string licenseKey =
                LicenseKeyTextBox.Text?
                    .Trim()
                ??
                string.Empty;


            if (
                string.IsNullOrWhiteSpace(
                    licenseKey
                )
            )
            {
                WpfMessageBox.Show(
                    "Please enter your license key.",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information
                );

                return;
            }


            if (
                !File.Exists(
                    _publicKeyPath
                )
            )
            {
                WpfMessageBox.Show(
                    $"License public key was not found.\n\n{_publicKeyPath}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
                );

                return;
            }


            bool valid =
                LicenseValidator.IsValid(
                    _deviceId,
                    licenseKey,
                    _publicKeyPath
                );


            if (!valid)
            {
                WpfMessageBox.Show(
                    "The license key is invalid for this device.",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning
                );

                return;
            }


            try
            {
                LicenseStorage.Save(
                    licenseKey
                );
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"License was valid, but it could not be saved.\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
                );

                return;
            }


            WpfMessageBox.Show(
                "License activated successfully.",
                "PrivateBrowser",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information
            );


            DialogResult =
                true;


            Close();
        }


        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            DialogResult =
                false;


            Close();
        }

        private async Task<bool> TrySetClipboardTextAsync(
    string text
)
        {
            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return false;
            }

            const int maxAttempts =
                12;

            const int delayMs =
                100;

            for (
                int attempt = 1;
                attempt <= maxAttempts;
                attempt++
            )
            {
                try
                {
                    WpfClipboard.SetText(
                        text
                    );

                    return true;
                }
                catch (
                    System.Runtime.InteropServices.COMException ex
                )
                when (
                    unchecked(
                        (uint)ex.HResult
                    )
                    ==
                    0x800401D0
                )
                {
                    await Task.Delay(
                        delayMs
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Clipboard error: {ex}"
                    );

                    return false;
                }
            }

            return false;
        }
    }
}