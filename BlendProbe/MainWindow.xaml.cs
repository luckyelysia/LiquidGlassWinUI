using System;
using System.Linq;
using BlendProbe.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BlendProbe
{
    // NavigationView shell. The pane drives a single content Frame; selecting
    // an item (or the built-in Settings item) navigates to the matching Page.
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TryResize(800, 600);
            // Select Home on launch — fires SelectionChanged → navigates.
            NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type pageType;
            if (args.IsSettingsSelected)
            {
                pageType = typeof(SettingsPage);
            }
            else
            {
                string tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
                pageType = tag switch
                {
                    "Home" => typeof(HomePage),
                    "Blend" => typeof(BlendPage),
                    "Combo" => typeof(ComboPage),
                    "MaskBind" => typeof(MaskBindingPage),
                    "GlassAB" => typeof(GlassABPage),
                    "BackdropLatency" => typeof(BackdropLatencyPage),
                    "MultiFlatten" => typeof(MultiFlattenPage),
                    _ => typeof(HomePage),
                };
            }

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }

        private void TryResize(int w, int h)
        {
            try { AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h)); } catch { }
        }
    }
}
