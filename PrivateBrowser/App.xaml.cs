using System;
using System.IO;
using System.Windows;

using WpfMessageBox =
    System.Windows.MessageBox;

using WpfMessageBoxButton =
    System.Windows.MessageBoxButton;

using WpfMessageBoxImage =
    System.Windows.MessageBoxImage;


namespace PrivateBrowser
{
    public partial class App :
        System.Windows.Application
    {
        protected override void OnStartup(
            StartupEventArgs e
        )
        {
            base.OnStartup(
                e
            );

            try
            {
                // =====================================================
                // DEVICE ID
                // =====================================================

                string deviceId =
                    DeviceFingerprint.GetDeviceId();


                // =====================================================
                // PUBLIC LICENSE KEY
                // =====================================================

                string publicKeyPath =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Keys",
                        "public-key.pem"
                    );


                if (
                    !File.Exists(
                        publicKeyPath
                    )
                )
                {
                    WpfMessageBox.Show(
                        $"License public key was not found.\n\n{publicKeyPath}",
                        "PrivateBrowser",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Error
                    );

                    Shutdown();

                    return;
                }


                // =====================================================
                // LOAD SAVED LICENSE
                // =====================================================

                string? storedLicense =
                    LicenseStorage.Load();


                bool licenseValid =
                    false;


                // =====================================================
                // VALIDATE SAVED LICENSE
                // =====================================================

                if (
                    !string.IsNullOrWhiteSpace(
                        storedLicense
                    )
                )
                {
                    licenseValid =
                        LicenseValidator.IsValid(
                            deviceId,
                            storedLicense,
                            publicKeyPath
                        );
                }


                // =====================================================
                // LICENSE NOT VALID
                // SHOW ACTIVATION WINDOW
                // =====================================================

                if (!licenseValid)
                {
                    // Delete invalid / unreadable stored license.
                    LicenseStorage.Delete();


                    ActivationWindow activationWindow =
                        new ActivationWindow();


                    bool? result =
                        activationWindow.ShowDialog();


                    if (
                        result !=
                        true
                    )
                    {
                        Shutdown();

                        return;
                    }


                    // The activation window validates and saves
                    // the license before returning true.
                }


                // =====================================================
                // OPEN MAIN WINDOW
                // =====================================================

                MainWindow mainWindow =
                    new MainWindow();


                MainWindow =
                    mainWindow;


                mainWindow.Show();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"PrivateBrowser could not start.\n\n{ex.Message}",
                    "PrivateBrowser",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error
                );


                Shutdown();
            }
        }
    }
}