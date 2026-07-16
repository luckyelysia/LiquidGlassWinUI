using LiquidGlassDemo.Pages;
using LiquidGlassWinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace LiquidGlassDemo;

public sealed partial class MainWindow : Window
{
    // Page cache avoids the WinUI 3 Frame.NavigateToType() crash on second
    // navigation with controls that own internal popups (ComboBox, ContentDialog).
    // NavigateToType() destroys and re-creates Page instances, but compositor
    // teardown of popup windows is asynchronous — a new instance created before
    // the old one fully tears down can hit stale compositor state (0xC0000005).
    private readonly Dictionary<Type, Page> _pageCache = new();
    private readonly DispatcherQueueTimer timer;

    public MainWindow()
    {
        InitializeComponent();

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += Timer_Tick;
        timer.Start();

        MainNavView.SelectionChanged += MainNavView_SelectionChanged;
        MainNavView.SelectedItem = MainNavView.MenuItems[0];
    }

    private void MainNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            var pageType = tag switch
            {
                "DevPage" => typeof(DevPage),
                "DialPage" => typeof(DialPage),
                "StressPage" => typeof(StressPage),
                "TabBarPage" => typeof(TabBarPage),
                "StoryboardPage" => typeof(StoryboardPage),
                _ => typeof(DevPage),
            };

            //if (!_pageCache.TryGetValue(pageType, out var page))
            //{
            //    page = (Page)Activator.CreateInstance(pageType)!;
            //    _pageCache[pageType] = page;
            //}
            //MainFrame.Content = page;
            MainFrame.Navigate(pageType);
        }
    }

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (string.IsNullOrEmpty(LiquidGlassBrush.LastError) == true) return;

        ShowError();
        timer.Tick -= Timer_Tick;
    }

    private async void ShowError()
    {
        var contentDialog = new ContentDialog
        {
            Title = "LiquidGlassBrush Error!",
            Content = LiquidGlassBrush.LastError,
            XamlRoot = this.Content.XamlRoot
        };
        await contentDialog.ShowAsync();
    }
}

