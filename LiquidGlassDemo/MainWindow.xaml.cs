using LiquidGlassDemo.Pages;
using LiquidGlassWinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace LiquidGlassDemo;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer timer;

    public MainWindow()
    {
        InitializeComponent();

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += Timer_Tick;
        timer.Start();

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            MainFrame.NavigateToType(typeof(DevPage), null, null);
        });
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

