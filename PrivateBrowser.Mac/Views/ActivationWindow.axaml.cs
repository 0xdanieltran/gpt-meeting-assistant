using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrivateBrowser.Mac.Views
{
    public partial class ActivationWindow : Window
    {
        private readonly string _deviceId;
        private readonly string _publicKeyPath;

        public bool ActivatedSuccessfully
        {
            get;
            private set;
        }

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
            object? sender,
            RoutedEventArgs e
        )
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(_deviceId);
            CopyDeviceIdButton.Content = "Copied";
        }

        private void ActivateButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            string licenseKey =
                LicenseKeyTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
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
                return;
            }

            LicenseStorage.Save(licenseKey);
            ActivatedSuccessfully = true;
            Close();
        }

        private void CancelButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            ActivatedSuccessfully = false;
            Close();
        }
    }
}
