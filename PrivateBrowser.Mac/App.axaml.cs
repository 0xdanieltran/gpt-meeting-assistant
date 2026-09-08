using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaWebView;
using PrivateBrowser.Mac.Views;

namespace PrivateBrowser.Mac
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void RegisterServices()
        {
            base.RegisterServices();
            AvaloniaWebViewBuilder.Initialize(default);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    string deviceId =
                        DeviceFingerprint.GetDeviceId();

                    string publicKeyPath =
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "Keys",
                            "public-key.pem"
                        );

                    if (!File.Exists(publicKeyPath))
                    {
                        desktop.Shutdown();
                        return;
                    }

                    string? storedLicense =
                        LicenseStorage.Load();

                    bool licenseValid =
                        !string.IsNullOrWhiteSpace(storedLicense)
                        &&
                        LicenseValidator.IsValid(
                            deviceId,
                            storedLicense!,
                            publicKeyPath
                        );

                    if (!licenseValid)
                    {
                        LicenseStorage.Delete();

                        ActivationWindow activation =
                            new ActivationWindow();

                        activation.Show();
                        activation.Closed += (_, _) =>
                        {
                            if (activation.ActivatedSuccessfully)
                            {
                                ShowMain(desktop);
                            }
                            else
                            {
                                desktop.Shutdown();
                            }
                        };

                        desktop.MainWindow = activation;
                    }
                    else
                    {
                        ShowMain(desktop);
                    }
                }
                catch
                {
                    desktop.Shutdown();
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static void ShowMain(
            IClassicDesktopStyleApplicationLifetime desktop
        )
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    MainWindow main =
                        new MainWindow();

                    desktop.MainWindow = main;
                    main.Show();
                }
            );
        }
    }
}
