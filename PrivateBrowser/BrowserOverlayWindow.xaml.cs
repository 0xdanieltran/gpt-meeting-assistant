using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace PrivateBrowser
{
    public partial class BrowserOverlayWindow : Window
    {
        public WebView2 WebViewControl =>
            WebView;

        public BrowserOverlayWindow()
        {
            InitializeComponent();
        }
    }
}
