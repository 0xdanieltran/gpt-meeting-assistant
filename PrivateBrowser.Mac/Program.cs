using Avalonia;
using Avalonia.WebView.Desktop;

namespace PrivateBrowser.Mac
{
    internal static class Program
    {
        [STAThread]
        public static void Main(
            string[] args
        )
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(
                    args
                );
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseDesktopWebView();
        }
    }
}
