using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CCSuitePro.Services;

/// <summary>
/// Render-only and bounded snowfall. Runs exclusively in the optional Winter Edition
/// theme, pauses when the window is minimized/hidden, and never accesses Mods or scans.
/// </summary>
internal sealed class WinterSnowfallEffect : IDisposable
{
    private const int MaxFlakes = 38;
    private readonly Canvas _canvas;
    private readonly Window _owner;
    private bool _rendering;
    private readonly List<Flake> _flakes = new();
    private readonly Random _random = new();
    private bool _enabled;
    private bool _disposed;
    private long _lastTick;
    private double _time;

    private sealed class Flake
    {
        internal required UIElement Visual;
        internal required TranslateTransform Transform;
        internal double X;
        internal double Y;
        internal double Velocity;
        internal double Drift;
        internal double Phase;
        internal double Radius;
    }

    internal WinterSnowfallEffect(Canvas canvas, Window owner)
    {
        _canvas = canvas;
        _owner = owner;
        // WPF calls Rendering at the display's rendering cadence. We update only
        // RenderTransform (no Canvas layout invalidation per frame). No threads or I/O.
        _canvas.IsHitTestVisible = false;
        _canvas.Loaded += CanvasLoaded;
        _canvas.Unloaded += CanvasUnloaded;
        _canvas.SizeChanged += CanvasSizeChanged;
        _owner.StateChanged += OwnerStateChanged;
        _owner.IsVisibleChanged += OwnerVisibilityChanged;
        // Pause decoration while another application has focus; resume without a jump.
        _owner.Activated += OwnerFocusChanged;
        _owner.Deactivated += OwnerFocusChanged;
    }

    internal void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        _enabled = enabled;
        RefreshRunning();
    }

    private void CanvasLoaded(object sender, RoutedEventArgs e) => RefreshRunning();
    private void CanvasUnloaded(object sender, RoutedEventArgs e) => RefreshRunning();
    private void CanvasSizeChanged(object sender, SizeChangedEventArgs e) => RefreshRunning();
    private void OwnerStateChanged(object? sender, EventArgs e) => RefreshRunning();
    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshRunning();
    private void OwnerFocusChanged(object? sender, EventArgs e) => RefreshRunning();

    private void RefreshRunning()
    {
        if (_disposed) return;
        bool run = _enabled && _canvas.IsLoaded && _owner.IsVisible && _owner.IsActive &&
                   SystemParameters.ClientAreaAnimation &&
                   _owner.WindowState != WindowState.Minimized &&
                   _canvas.ActualWidth > 100 && _canvas.ActualHeight > 100;
        if (!run)
        {
            StopRendering();
            if (!_enabled)
            {
                _canvas.Children.Clear();
                _flakes.Clear();
            }
            return;
        }
        if (_flakes.Count == 0) CreateFlakes();
        if (!_rendering)
        {
            _lastTick = Stopwatch.GetTimestamp();
            _rendering = true;
            CompositionTarget.Rendering += Animate;
        }
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= Animate;
        _rendering = false;
    }

    private void CreateFlakes()
    {
        double width = _canvas.ActualWidth;
        double height = _canvas.ActualHeight;
        for (int i = 0; i < MaxFlakes; i++)
        {
            bool crystal = i % 5 == 0;
            double radius = crystal ? 11 + _random.NextDouble() * 6
                                    : 2.0 + _random.NextDouble() * 3.8;
            UIElement visual;
            if (crystal)
            {
                visual = new TextBlock
                {
                    Text = "❄",
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = radius,
                    Foreground = new SolidColorBrush(Color.FromArgb(125, 242, 249, 255)),
                    IsHitTestVisible = false
                };
            }
            else
            {
                visual = new Ellipse
                {
                    Width = radius,
                    Height = radius,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)_random.Next(66, 148), 245, 249, 255)),
                    IsHitTestVisible = false
                };
            }
            var translate = new TranslateTransform();
            visual.RenderTransform = translate;
            var flake = new Flake
            {
                Visual = visual,
                Transform = translate,
                X = _random.NextDouble() * width,
                Y = _random.NextDouble() * height,
                // Higher fall speed than Winter 18: ~65-145 px/s instead of 13-51.
                Velocity = 65 + _random.NextDouble() * 80,
                Drift = 10 + _random.NextDouble() * 18,
                Phase = _random.NextDouble() * Math.PI * 2,
                Radius = radius
            };
            _canvas.Children.Add(visual);
            Canvas.SetLeft(visual, 0);
            Canvas.SetTop(visual, 0);
            translate.X = flake.X;
            translate.Y = flake.Y;
            _flakes.Add(flake);
        }
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (_disposed || !_enabled || !_owner.IsVisible || !_owner.IsActive ||
            !SystemParameters.ClientAreaAnimation ||
            _owner.WindowState == WindowState.Minimized || !_canvas.IsLoaded)
        {
            RefreshRunning();
            return;
        }
        long tick = Stopwatch.GetTimestamp();
        // Time-based speed stays consistent on 60/120 Hz displays; clamp long UI
        // stalls to prevent a sudden jump when returning from a blocked frame.
        double seconds = Math.Clamp((double)(tick - _lastTick) / Stopwatch.Frequency, 0.0, 0.08);
        if (seconds <= 0.0001) return;
        _lastTick = tick;
        _time += seconds;
        double width = _canvas.ActualWidth;
        double height = _canvas.ActualHeight;
        if (width <= 100 || height <= 100) { StopRendering(); return; }
        foreach (Flake flake in _flakes)
        {
            flake.Y += flake.Velocity * seconds;
            flake.X += Math.Sin(_time * 1.2 + flake.Phase) * flake.Drift * seconds;
            if (flake.Y > height + flake.Radius)
            {
                flake.Y = -flake.Radius - _random.NextDouble() * 30;
                flake.X = _random.NextDouble() * width;
            }
            if (flake.X > width + flake.Radius) flake.X = -flake.Radius;
            if (flake.X < -flake.Radius) flake.X = width;
            flake.Transform.X = flake.X;
            flake.Transform.Y = flake.Y;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopRendering();
        _canvas.Loaded -= CanvasLoaded;
        _canvas.Unloaded -= CanvasUnloaded;
        _canvas.SizeChanged -= CanvasSizeChanged;
        _owner.StateChanged -= OwnerStateChanged;
        _owner.IsVisibleChanged -= OwnerVisibilityChanged;
        _owner.Activated -= OwnerFocusChanged;
        _owner.Deactivated -= OwnerFocusChanged;
        _flakes.Clear();
        _canvas.Children.Clear();
        _disposed = true;
    }
}

/// <summary>User-only UI preference, separate from app data, Mods, and scan cache.</summary>
internal static class WinterSnowfallPreference
{
    private static readonly string PreferencePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCSuitePro", "Preferences", "winter.snowfall");

    internal static bool ReadEnabled()
    {
        try
        {
            if (File.Exists(PreferencePath))
                return !string.Equals(File.ReadAllText(PreferencePath).Trim(), "off", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }

    internal static void SaveEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, enabled ? "on" : "off");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
