using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace CCSuitePro.Services;

/// <summary>
/// Optional, bounded Winter Edition decorations. Pure WPF visuals on the UI thread;
/// no background processes, network access, scans, Mods changes, or file enumeration.
/// </summary>
internal enum WinterLightsMode { Off = 0, Soft = 1, Dynamic = 2 }

internal sealed class WinterAmbientEffect : IDisposable
{
    // Larger decorative pendants: density scales down on narrow windows.
    private const int SparkleCount = 2;
    private readonly Canvas _canvas;
    private readonly Window _owner;
    private bool _rendering;
    private readonly Random _random = new();
    private readonly List<Lamp> _lamps = new();
    private readonly List<Sparkle> _sparkles = new();
    // WPF StreamGeometry is immutable and frozen: reuse the three ornaments
    // when the window changes size instead of allocating fresh paths.
    private static readonly Geometry[] OrnamentShapes =
    {
        OrnamentGeometry(0), OrnamentGeometry(1), OrnamentGeometry(2)
    };
    private WinterLightsMode _mode = WinterLightsMode.Off;
    private bool _disposed;
    private double _width;
    private double _height;
    private double _time;
    private long _lastTick;

    private sealed class Lamp
    {
        internal required Ellipse Bulb;
        internal required Ellipse Halo;
        internal required Ellipse Highlight;
        internal required ShapePath Ornament;
        internal required ShapePath SoftEdge;
        internal int Index;
        internal double Phase;
        internal double Speed;
    }

    private sealed class Sparkle
    {
        internal required TextBlock Visual;
        internal double Phase;
        internal double Speed;
    }

    internal WinterAmbientEffect(Canvas canvas, Window owner)
    {
        _canvas = canvas;
        _owner = owner;
        _canvas.IsHitTestVisible = false;
        // WPF render-frame animation updates only opacity of a bounded set of visuals.
        // No worker thread, network, file scans, timers or blocking delays.
        _canvas.Loaded += CanvasLoaded;
        _canvas.Unloaded += CanvasUnloaded;
        _canvas.SizeChanged += CanvasSizeChanged;
        _owner.StateChanged += OwnerStateChanged;
        _owner.IsVisibleChanged += OwnerVisibilityChanged;
        _owner.Activated += OwnerFocusChanged;
        _owner.Deactivated += OwnerFocusChanged;
    }

    internal void SetMode(WinterLightsMode mode)
    {
        if (_disposed) return;
        _mode = mode;
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
        bool run = _mode != WinterLightsMode.Off && _canvas.IsLoaded && _owner.IsVisible &&
                   _owner.IsActive && SystemParameters.ClientAreaAnimation &&
                   _owner.WindowState != WindowState.Minimized &&
                   _canvas.ActualWidth > 360 && _canvas.ActualHeight > 280;
        if (!run)
        {
            StopRendering();
            if (_mode == WinterLightsMode.Off) ClearVisuals();
            return;
        }
        double w = _canvas.ActualWidth;
        double h = _canvas.ActualHeight;
        if (_lamps.Count == 0 || Math.Abs(w - _width) > 64 || Math.Abs(h - _height) > 80)
            BuildVisuals(w, h);
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

    private void ClearVisuals()
    {
        _canvas.Children.Clear();
        _lamps.Clear();
        _sparkles.Clear();
        _width = 0;
        _height = 0;
    }

    private static RadialGradientBrush HaloBrush(Color color)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(color, 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
        brush.Freeze();
        return brush;
    }

    private void AddCornerGlow(double left, double top, double size, Color color)
    {
        var glow = new Ellipse
        {
            Width = size,
            Height = size * 0.74,
            Fill = HaloBrush(color),
            IsHitTestVisible = false
        };
        _canvas.Children.Add(glow);
        Canvas.SetLeft(glow, left);
        Canvas.SetTop(glow, top);
    }

    private void BuildVisuals(double width, double height)
    {
        ClearVisuals();
        _width = width;
        _height = height;

        // Restrained ambient corner reflections, far below content contrast.
        AddCornerGlow(width - 265, -80, 310, Color.FromArgb(30, 255, 178, 99));
        AddCornerGlow(-110, height - 170, 260, Color.FromArgb(25, 173, 221, 246));

        // Reserve a generous protected zone at both ends of the titlebar:
        // app title on the left, Help/Minimize/Maximize/Close on the right.
        // Include the widest halo in these bounds. The decorations never
        // intercept pointer input and disappear when there isn't enough room.
        // Keep the first pendant clear of the Dashboard title; also reserve
        // a generous 330+ DIP margin for window controls on the right.
        double startX = Math.Max(455.0, width * 0.33);
        double endX = width - Math.Max(330.0, width * 0.25);
        if (width >= 1100.0 && endX - startX >= 325.0)
        {
        int count = Math.Clamp((int)((endX - startX) / 84.0) + 1, 5, 9);
        double gap = (endX - startX) / (count - 1);

        // Gently drooping arcs, fully visible below the top clipping edge.
        // The glow and pendants stay in the compact header strip while the
        // left and right titlebar control regions stay completely clear.
        var cordShadow = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(90, 95, 67, 46)),
            StrokeThickness = 2.0,
            IsHitTestVisible = false
        };
        var cordGold = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(195, 207, 154, 83)),
            StrokeThickness = 1.0,
            IsHitTestVisible = false
        };
        for (int step = 0; step <= (count - 1) * 8; step++)
        {
            double pos = step / 8.0;
            double x = startX + gap * pos;
            double y = GarlandY(pos);
            cordShadow.Points.Add(new Point(x, y));
            cordGold.Points.Add(new Point(x, y - 0.6));
        }
        _canvas.Children.Add(cordShadow);
        _canvas.Children.Add(cordGold);

        // A compact pine-and-berry accent at each end, plus a tiny ribbon.
        // These are all vector WPF shapes within the protected top strip.
        AddEvergreenSprig(startX + 9.0, 8.5, mirrored: false);
        AddEvergreenSprig(endX - 51.0, 8.5, mirrored: true);
        if (count >= 7)
        {
            double ribbonX = startX + gap * ((count - 1) / 2.0 + 0.5);
            AddHolidayRibbon(ribbonX, 9.5);
        }

        // Primarily golden incandescent Christmas lights, with a few warm
        // ivory variations; no remote images, sounds, GPU shaders or timers.
        Color[] golds =
        {
            Color.FromRgb(255, 202, 99),
            Color.FromRgb(255, 225, 151),
            Color.FromRgb(255, 187, 80),
            Color.FromRgb(255, 241, 197)
        };
        for (int i = 0; i < count; i++)
        {
            double x = startX + gap * i;
            double topY = GarlandY(i);
            double size = i % 4 == 0 ? 17.0 : (i % 3 == 0 ? 16.0 : 14.5);
            double pendantCenterY = topY + 10.0 + (i % 3) * 0.8;
            Color color = golds[i % golds.Length];
            Color fog = Color.FromArgb(175, color.R, color.G, color.B);

            var hanger = new Line
            {
                X1 = x, X2 = x, Y1 = topY, Y2 = pendantCenterY - size / 2.0 + 1.0,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 206, 159, 99)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(hanger);

            var halo = new Ellipse
            {
                Width = size * 1.8,
                Height = size * 1.8,
                Fill = HaloBrush(fog),
                IsHitTestVisible = false
            };
            _canvas.Children.Add(halo);
            Canvas.SetLeft(halo, x - halo.Width / 2.0);
            Canvas.SetTop(halo, pendantCenterY - halo.Height / 2.0);

            Geometry geometry = OrnamentShapes[i % OrnamentShapes.Length];
            double left = x - size / 2.0;
            double top = pendantCenterY - size / 2.0;
            var softEdge = new ShapePath
            {
                Data = geometry, Width = size, Height = size,
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3.8,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                Opacity = 0.25,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(softEdge);
            Canvas.SetLeft(softEdge, left);
            Canvas.SetTop(softEdge, top);

            var ornament = new ShapePath
            {
                Data = geometry, Width = size, Height = size,
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.55,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = i % 3 == 1 ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B)),
                IsHitTestVisible = false
            };
            _canvas.Children.Add(ornament);
            Canvas.SetLeft(ornament, left);
            Canvas.SetTop(ornament, top);

            // A luminous LED core makes the symbol read as a real string light.
            var bulb = new Ellipse
            {
                Width = 4.0, Height = 4.0,
                Fill = new SolidColorBrush(Color.FromRgb(255, 239, 190)),
                IsHitTestVisible = false
            };
            _canvas.Children.Add(bulb);
            Canvas.SetLeft(bulb, x - 2);
            Canvas.SetTop(bulb, pendantCenterY - 2);
            var highlight = new Ellipse
            {
                Width = 1.9, Height = 1.9,
                Fill = Brushes.White,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(highlight);
            Canvas.SetLeft(highlight, x - 1.5);
            Canvas.SetTop(highlight, pendantCenterY - 1.8);

            _lamps.Add(new Lamp
            {
                Bulb = bulb,
                Halo = halo,
                Highlight = highlight,
                Ornament = ornament,
                SoftEdge = softEdge,
                Index = i,
                Phase = (_random.NextDouble() * Math.PI * 2) + i * 0.39,
                Speed = 0.90 + _random.NextDouble() * 1.12
            });
        }

        } // End of reserved, titlebar-safe decorative strip.

        // A handful of small highlights only in the decorative header strip;
        // none scattered over Settings controls, cards or mod filenames.
        // On a narrow window this section is skipped as well.
        if (width >= 1100.0 && endX - startX >= 325.0)
        {
        double available = Math.Max(10, endX - startX);
        for (int i = 0; i < SparkleCount; i++)
        {
            var sparkle = new TextBlock
            {
                Text = i % 4 == 0 ? "✧" : "✦",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 8 + _random.NextDouble() * 6,
                Foreground = new SolidColorBrush(i % 3 == 0
                    ? Color.FromRgb(255, 207, 169)
                    : Color.FromRgb(218, 237, 251)),
                IsHitTestVisible = false,
                Opacity = 0.0
            };
            _canvas.Children.Add(sparkle);
            Canvas.SetLeft(sparkle, startX + _random.NextDouble() * available);
            Canvas.SetTop(sparkle, 11.0 + _random.NextDouble() * 22.0);
            _sparkles.Add(new Sparkle
            {
                Visual = sparkle,
                Phase = _random.NextDouble() * Math.PI * 2,
                Speed = 0.30 + _random.NextDouble() * 0.60
            });
        }
        }
    }

    private void AddEvergreenSprig(double x, double y, bool mirrored)
    {
        // One vector drawing holds the twig and all needles, rather than
        // creating dozens of individual WPF controls on every resize.
        var needles = new StreamGeometry();
        using (StreamGeometryContext ctx = needles.Open())
        {
            ctx.BeginFigure(new Point(2, 8), false, false);
            ctx.LineTo(new Point(42, 9.2), true, false);
            for (int i = 0; i < 9; i++)
            {
                double px = 5.0 + i * 4.2;
                double py = 8.0 + 1.1 * Math.Sin(i * 0.34);
                ctx.BeginFigure(new Point(px, py), false, false);
                ctx.LineTo(new Point(px - 3.8, py - 5.8 - (i % 3) * 0.6), true, false);
                ctx.BeginFigure(new Point(px, py), false, false);
                ctx.LineTo(new Point(px + 2.6, py + 5.4 + (i % 2) * 0.6), true, false);
            }
        }
        needles.Freeze();
        var pine = new ShapePath
        {
            Width = 44, Height = 17,
            Stretch = Stretch.Fill,
            Data = needles,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 100, 145, 85)),
            StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = new ScaleTransform(mirrored ? -1 : 1, 1, 22, 8.5),
            IsHitTestVisible = false
        };
        _canvas.Children.Add(pine);
        Canvas.SetLeft(pine, x);
        Canvas.SetTop(pine, y);

        // Three opaque red berries; static and constrained to the title strip.
        (double bx, double by)[] berries = { (12.0, 6.3), (17.0, 9.4), (23.5, 5.1) };
        foreach (var (bx, by) in berries)
        {
            double localX = mirrored ? 44.0 - bx : bx;
            var berry = new Ellipse
            {
                Width = 4.5, Height = 4.5,
                Fill = new SolidColorBrush(Color.FromRgb(185, 56, 64)),
                Stroke = new SolidColorBrush(Color.FromArgb(180, 242, 130, 112)),
                StrokeThickness = 0.6,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(berry);
            Canvas.SetLeft(berry, x + localX - 2.25);
            Canvas.SetTop(berry, y + by - 2.25);
        }
    }

    private void AddHolidayRibbon(double centerX, double y)
    {
        // Small red bow between two pendants (28x22 DIP), offset downward
        // so the loops and tails are not clipped at the screen's top edge.
        var ribbonFill = new SolidColorBrush(Color.FromRgb(157, 42, 56));
        var trim = new SolidColorBrush(Color.FromRgb(221, 164, 106));
        var left = new Polygon
        {
            Points = new PointCollection
            {
                new Point(13, 7), new Point(1, 1), new Point(2, 12)
            },
            Fill = ribbonFill, Stroke = trim, StrokeThickness = 0.9,
            IsHitTestVisible = false
        };
        var right = new Polygon
        {
            Points = new PointCollection
            {
                new Point(13, 7), new Point(25, 1), new Point(24, 12)
            },
            Fill = ribbonFill, Stroke = trim, StrokeThickness = 0.9,
            IsHitTestVisible = false
        };
        var tails = new Polygon
        {
            Points = new PointCollection
            {
                new Point(11, 9), new Point(7, 21), new Point(11, 18),
                new Point(14, 21), new Point(15, 9)
            },
            Fill = ribbonFill, Stroke = trim, StrokeThickness = 0.9,
            IsHitTestVisible = false
        };
        foreach (Polygon part in new[] { tails, left, right })
        {
            _canvas.Children.Add(part);
            Canvas.SetLeft(part, centerX - 13);
            Canvas.SetTop(part, y);
        }
        var knot = new Ellipse
        {
            Width = 5.5, Height = 5.5,
            Fill = new SolidColorBrush(Color.FromRgb(216, 90, 97)),
            Stroke = trim, StrokeThickness = 0.8,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(knot);
        Canvas.SetLeft(knot, centerX - 2.75);
        Canvas.SetTop(knot, y + 4.0);
    }

    private static double GarlandY(double position)
    {
        // Hanging swags between supports 3.5 lamps apart. Each successive
        // curved section has a slightly different sag, with smooth endpoints.
        const double lampsPerSwag = 3.5;
        double swag = Math.Floor(position / lampsPerSwag);
        double local = (position - swag * lampsPerSwag) / lampsPerSwag;
        double sag = 8.8 + (swag % 3.0) * 1.2;
        return 13.0 + sag * Math.Sin(Math.PI * local);
    }

    private static Geometry OrnamentGeometry(int kind)
    {
        // Reusable, vector-only WPF path. All symbols fit inside a 20x20 box.
        var shape = new StreamGeometry();
        using (StreamGeometryContext ctx = shape.Open())
        {
            if (kind == 0) // five-point Christmas star
            {
                for (int i = 0; i < 10; i++)
                {
                    double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
                    double r = i % 2 == 0 ? 8.8 : 4.0;
                    var point = new Point(10 + r * Math.Cos(angle),
                                          10 + r * Math.Sin(angle));
                    if (i == 0) ctx.BeginFigure(point, true, true);
                    else ctx.LineTo(point, true, false);
                }
            }
            else if (kind == 1) // six-armed crystalline snowflake
            {
                for (int arm = 0; arm < 6; arm++)
                {
                    double angle = arm * Math.PI / 3.0;
                    double ux = Math.Cos(angle), uy = Math.Sin(angle);
                    Point center = new(10, 10);
                    ctx.BeginFigure(center, false, false);
                    ctx.LineTo(new Point(10 + ux * 8.6, 10 + uy * 8.6), true, false);
                    double ox = 10 + ux * 5.8, oy = 10 + uy * 5.8;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        double side = angle + sign * 0.82;
                        ctx.BeginFigure(new Point(ox, oy), false, false);
                        ctx.LineTo(new Point(ox - 3.1 * Math.Cos(side),
                                             oy - 3.1 * Math.Sin(side)), true, false);
                    }
                }
            }
            else // tiered fir tree and little trunk
            {
                Point[] outline =
                {
                    new(10, 1), new(14.1, 7), new(12.1, 7),
                    new(16.3, 12.4), new(13.9, 12.4),
                    new(18, 17), new(11.6, 17), new(11.6, 19.5),
                    new(8.4, 19.5), new(8.4, 17), new(2, 17),
                    new(6.1, 12.4), new(3.7, 12.4),
                    new(7.9, 7), new(5.9, 7)
                };
                ctx.BeginFigure(outline[0], true, true);
                for (int i = 1; i < outline.Length; i++)
                    ctx.LineTo(outline[i], true, false);
            }
        }
        shape.Freeze();
        return shape;
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (_disposed || _mode == WinterLightsMode.Off || !_owner.IsVisible ||
            !_owner.IsActive || !SystemParameters.ClientAreaAnimation ||
            _owner.WindowState == WindowState.Minimized || !_canvas.IsLoaded)
        {
            RefreshRunning();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        double delta = Math.Clamp((double)(now - _lastTick) / Stopwatch.Frequency, 0.0, 0.08);
        // ~30 updates/s is smooth for decorative opacity and avoids needlessly
        // evaluating every lamp on 120/144Hz displays.
        if (delta < 1.0 / 35.0) return;
        _lastTick = now;
        _time += delta;
        bool dynamic = _mode == WinterLightsMode.Dynamic;
        int count = Math.Max(1, _lamps.Count);
        double wavePosition = (_time * 3.2) % count;
        foreach (Lamp lamp in _lamps)
        {
            double breathing = 0.5 + 0.5 * Math.Sin(_time * lamp.Speed + lamp.Phase);
            double distance = Math.Abs(lamp.Index - wavePosition);
            distance = Math.Min(distance, count - distance);
            double travelingWave = dynamic ? Math.Exp(-0.83 * distance * distance) : 0.0;
            double glow = Math.Clamp((dynamic ? 0.36 + 0.36 * breathing + 0.29 * travelingWave
                                             : 0.42 + 0.20 * breathing), 0.0, 1.0);
            lamp.Bulb.Opacity = 0.42 + 0.58 * glow;
            lamp.Halo.Opacity = (dynamic ? 0.20 : 0.12) + (dynamic ? 0.55 : 0.28) * glow;
            lamp.Highlight.Opacity = 0.30 + 0.65 * glow;
            lamp.Ornament.Opacity = 0.62 + 0.38 * glow;
            lamp.SoftEdge.Opacity = (dynamic ? 0.13 : 0.08) + 0.43 * glow;
        }
        foreach (Sparkle sparkle in _sparkles)
        {
            double pulse = Math.Max(0, Math.Sin(_time * sparkle.Speed + sparkle.Phase));
            sparkle.Visual.Opacity = dynamic ? 0.03 + 0.26 * pulse * pulse
                                            : 0.02 + 0.12 * pulse * pulse;
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
        ClearVisuals();
        _disposed = true;
    }
}

/// <summary>Cosmetic mode preference. Backward compatible with Winter 19's "on/off".</summary>
internal static class WinterAmbientPreference
{
    private static readonly string PreferencePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CCSuitePro", "Preferences", "winter.ambient");

    internal static WinterLightsMode ReadMode()
    {
        try
        {
            if (File.Exists(PreferencePath))
            {
                string value = File.ReadAllText(PreferencePath).Trim().ToLowerInvariant();
                if (value == "off") return WinterLightsMode.Off;
                if (value == "soft") return WinterLightsMode.Soft;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Existing "on" preference and fresh installs default to the lively mode.
        return WinterLightsMode.Dynamic;
    }

    internal static void SaveMode(WinterLightsMode mode)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, mode switch
            {
                WinterLightsMode.Off => "off",
                WinterLightsMode.Soft => "soft",
                _ => "dynamic"
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
