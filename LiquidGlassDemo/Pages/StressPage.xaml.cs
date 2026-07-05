using System;
using System.Collections.Generic;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LiquidGlassDemo.Pages;

public sealed partial class StressPage : Page
{
    private int _count;
    private int _failedAt = -1;

    public StressPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StatusText.Text = "Creating…";

        await System.Threading.Tasks.Task.Delay(200);
        PumpBrushes();
    }

    private async void PumpBrushes()
    {
        const int cols = 25;
        const int batchRows = 4;    // layout once per N rows
        const int batchSize = cols * batchRows;
        const int maxTotal = 4096;

        var pending = new List<Border>(batchSize);
        StackPanel currentRow = null;

        while (_count < maxTotal && _failedAt < 0)
        {
            pending.Clear();

            for (int i = 0; i < batchSize && _count < maxTotal; i++)
            {
                try
                {
                    var brush = new LiquidGlassBrush
                    {
                        BlurAmount = 0,
                        RefThickness = 20,
                        RefFactor = 1.8,
                        ShapeRadius = 1,
                        ShapeRoundness = 0,
                    };

                    var border = new Border
                    {
                        Width = 48,
                        Height = 48,
                        CornerRadius = new CornerRadius(24),
                        Background = brush,
                    };

                    pending.Add(border);
                    _count++;
                }
                catch (Exception ex)
                {
                    _failedAt = _count;
                    StatusText.Text = $"FAILED at {_failedAt} — {ex.Message}";
                    return;
                }
            }

            // Batch layout: add all pending borders to rows, then add rows to panel once.
            foreach (var border in pending)
            {
                if (currentRow == null || currentRow.Children.Count >= cols)
                {
                    currentRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    HostPanel.Children.Add(currentRow);
                }
                currentRow.Children.Add(border);
            }

            StatusText.Text = $"Created: {_count}";
            await System.Threading.Tasks.Task.Delay(30);
        }

        if (_failedAt < 0)
            StatusText.Text = $"{_count} brushes — ok (max {maxTotal})";
    }
}
