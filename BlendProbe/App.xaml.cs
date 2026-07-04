using Microsoft.UI.Xaml;

namespace BlendProbe
{
    // Unpackaged, self-contained WinUI 3 entry point. The Windows App SDK targets
    // inject the bootstrap/auto-initializer (WindowsAppSDKSelfContained=true), so
    // OnLaunched only needs to surface the window.
    public partial class App : Application
    {
        private Window _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
