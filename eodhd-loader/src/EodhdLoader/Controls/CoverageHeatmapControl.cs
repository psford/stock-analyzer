using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EodhdLoader.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace EodhdLoader.Controls;

/// <summary>
/// Custom bivariate coverage heatmap rendered with SkiaSharp.
/// X-axis: Year (1980-2026), Y-axis: ImportanceScore (1-10).
/// Color: Blue = tracked, Yellow = untracked, Green = both, Dark = empty.
/// </summary>
public class CoverageHeatmapControl : UserControl
{
    // Dependency properties for data binding
    public static readonly DependencyProperty HeatmapDataProperty =
        DependencyProperty.Register(nameof(HeatmapData), typeof(HeatmapDataResult), typeof(CoverageHeatmapControl),
            new PropertyMetadata(null, OnHeatmapDataChanged));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(CoverageHeatmapControl),
            new PropertyMetadata(false, OnIsLoadingChanged));

    public HeatmapDataResult? HeatmapData
    {
        get => (HeatmapDataResult?)GetValue(HeatmapDataProperty);
        set => SetValue(HeatmapDataProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly DependencyProperty ActiveYearProperty =
        DependencyProperty.Register(nameof(ActiveYear), typeof(int), typeof(CoverageHeatmapControl),
            new PropertyMetadata(-1, OnActiveCellChanged));

    public static readonly DependencyProperty ActiveScoreProperty =
        DependencyProperty.Register(nameof(ActiveScore), typeof(int), typeof(CoverageHeatmapControl),
            new PropertyMetadata(-1, OnActiveCellChanged));

    public int ActiveYear
    {
        get => (int)GetValue(ActiveYearProperty);
        set => SetValue(ActiveYearProperty, value);
    }

    public int ActiveScore
    {
        get => (int)GetValue(ActiveScoreProperty);
        set => SetValue(ActiveScoreProperty, value);
    }

    // Anchor colors for bivariate mapping
    private static readonly SKColor BlueTint = new(30, 100, 255);     // Tracked only
    private static readonly SKColor GreenTint = new(30, 220, 60);     // Both present
    private static readonly SKColor YellowTint = new(255, 200, 30);   // Untracked only
    private static readonly SKColor DarkBg = new(10, 10, 21);         // Empty cell
    private static readonly SKColor CardBg = new(26, 26, 46);         // #1a1a2e
    private static readonly SKTypeface ConsolasTypeface = SKTypeface.FromFamilyName("Consolas");

    // Layout constants
    private const float LeftMargin = 65f;
    private const float BottomMargin = 55f;
    private const float TopMargin = 10f;
    private const float RightMargin = 180f;
    private const float CellPadding = 1f;

    // Pre-computed data for rendering
    private Dictionary<(int year, int score), HeatmapCell>? _cellLookup;
    private int _minYear, _maxYear;
    private long _maxTracked, _maxUntracked;

    // Hover state
    private int _hoverYear = -1;
    private int _hoverScore = -1;

    // Pulse animation state
    private readonly DispatcherTimer _pulseTimer;
    private double _pulsePhase; // 0.0 to 2π, drives smooth sine-wave pulse
    private const double PulseSpeed = 0.15; // radians per tick (~60fps feel)

    private readonly SKElement _skElement;
    private readonly ToolTip _tooltip;

    public CoverageHeatmapControl()
    {
        _skElement = new SKElement();
        _skElement.PaintSurface += OnPaintSurface;

        _tooltip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 46)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 80)),
            Padding = new Thickness(8, 6, 8, 6)
        };

        // Pulse animation timer (~30fps for smooth glow)
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulsePhase += PulseSpeed;
            if (_pulsePhase > 2 * Math.PI) _pulsePhase -= 2 * Math.PI;
            _skElement.InvalidateVisual();
        };

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;

        Content = _skElement;
    }

    private static void OnHeatmapDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverageHeatmapControl control)
        {
            control.PrepareData();
            control._skElement.InvalidateVisual();
        }
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverageHeatmapControl control)
        {
            control._skElement.InvalidateVisual();
        }
    }

    private static void OnActiveCellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CoverageHeatmapControl control)
        {
            bool hasActiveCell = control.ActiveYear >= 0 && control.ActiveScore >= 0;
            if (hasActiveCell && !control._pulseTimer.IsEnabled)
            {
                control._pulsePhase = 0;
                control._pulseTimer.Start();
            }
            else if (!hasActiveCell && control._pulseTimer.IsEnabled)
            {
                control._pulseTimer.Stop();
                control._skElement.InvalidateVisual();
            }
        }
    }

    private void PrepareData()
    {
        var data = HeatmapData;
        if (data?.Cells == null || data.Metadata == null || data.Cells.Count == 0)
        {
            _cellLookup = null;
            return;
        }

        _cellLookup = new Dictionary<(int, int), HeatmapCell>();
        foreach (var cell in data.Cells)
        {
            _cellLookup[(cell.Year, cell.Score)] = cell;
        }

        _minYear = data.Metadata.MinYear;
        _maxYear = data.Metadata.MaxYear;
        _maxTracked = Math.Max(1, data.Metadata.MaxTrackedRecords);
        _maxUntracked = Math.Max(1, data.Metadata.MaxUntrackedRecords);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(DarkBg);

        if (IsLoading)
        {
            DrawCenteredText(canvas, info, "Loading heatmap data...", 14, new SKColor(136, 136, 136));
            return;
        }

        if (_cellLookup == null || _cellLookup.Count == 0)
        {
            DrawCenteredText(canvas, info, "Connect to API to load heatmap data", 14, new SKColor(136, 136, 136));
            return;
        }

        DrawGrid(canvas, info);
        DrawAxisLabels(canvas, info);
        DrawBivariateLegend(canvas, info);
    }

    private void DrawGrid(SKCanvas canvas, SKImageInfo info)
    {
        int yearSpan = _maxYear - _minYear + 1;
        int scoreCount = 10; // ImportanceScore 1-10

        float gridWidth = info.Width - LeftMargin - RightMargin;
        float gridHeight = info.Height - TopMargin - BottomMargin;

        float cellWidth = (gridWidth - (yearSpan - 1) * CellPadding) / yearSpan;
        float cellHeight = (gridHeight - (scoreCount - 1) * CellPadding) / scoreCount;

        using var paint = new SKPaint { IsAntialias = false };

        for (int yi = 0; yi < yearSpan; yi++)
        {
            int year = _minYear + yi;
            for (int si = 0; si < scoreCount; si++)
            {
                int score = 10 - si; // Score 10 at top, 1 at bottom

                var color = ComputeCellColor(year, score);
                paint.Color = color;

                float x = LeftMargin + yi * (cellWidth + CellPadding);
                float y = TopMargin + si * (cellHeight + CellPadding);

                canvas.DrawRect(x, y, cellWidth, cellHeight, paint);

                // Pulsing glow on active cell (currently loading data)
                if (year == ActiveYear && score == ActiveScore)
                {
                    // Sine wave pulse: alpha oscillates between 60 and 220
                    double pulse = (Math.Sin(_pulsePhase) + 1.0) / 2.0; // 0.0 to 1.0
                    byte glowAlpha = (byte)(60 + pulse * 160);

                    // Outer glow (expanded rect)
                    using var glowPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        Color = new SKColor(255, 255, 255, (byte)(glowAlpha / 2)),
                        StrokeWidth = 4
                    };
                    canvas.DrawRect(x - 2, y - 2, cellWidth + 4, cellHeight + 4, glowPaint);

                    // Inner bright border
                    using var pulsePaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        Color = new SKColor(0, 255, 200, glowAlpha),
                        StrokeWidth = 2
                    };
                    canvas.DrawRect(x, y, cellWidth, cellHeight, pulsePaint);
                }
                // Highlight hovered cell
                else if (year == _hoverYear && score == _hoverScore)
                {
                    using var highlightPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        Color = new SKColor(255, 255, 255, 180),
                        StrokeWidth = 2
                    };
                    canvas.DrawRect(x, y, cellWidth, cellHeight, highlightPaint);
                }
            }
        }
    }

    private SKColor ComputeCellColor(int year, int score)
    {
        if (_cellLookup == null || !_cellLookup.TryGetValue((year, score), out var cell))
            return DarkBg;

        if (cell.TrackedRecords == 0 && cell.UntrackedRecords == 0)
            return DarkBg;

        // Normalize and apply perceptual scaling (sqrt)
        double t = Math.Sqrt(Math.Min(1.0, (double)cell.TrackedRecords / _maxTracked));
        double u = Math.Sqrt(Math.Min(1.0, (double)cell.UntrackedRecords / _maxUntracked));

        // Ratio determines hue: 0 = all untracked (yellow), 1 = all tracked (blue)
        double sum = t + u;
        double ratio = sum > 0 ? t / sum : 0.5;

        // Piecewise interpolation through green midpoint
        SKColor baseColor;
        if (ratio >= 0.5)
        {
            // Blue to Green: ratio 0.5→1.0 maps to Green→Blue
            double f = (ratio - 0.5) * 2.0;
            baseColor = LerpColor(GreenTint, BlueTint, f);
        }
        else
        {
            // Yellow to Green: ratio 0.0→0.5 maps to Yellow→Green
            double f = ratio * 2.0;
            baseColor = LerpColor(YellowTint, GreenTint, f);
        }

        // Brightness from total coverage intensity
        double brightness = Math.Min(1.0, (t + u) / 2.0);
        // Boost minimum brightness so non-zero cells are always visible
        brightness = 0.15 + brightness * 0.85;

        return LerpColor(DarkBg, baseColor, brightness);
    }

    private static SKColor LerpColor(SKColor a, SKColor b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * f),
            (byte)(a.Green + (b.Green - a.Green) * f),
            (byte)(a.Blue + (b.Blue - a.Blue) * f));
    }

    private void DrawAxisLabels(SKCanvas canvas, SKImageInfo info)
    {
        int yearSpan = _maxYear - _minYear + 1;
        int scoreCount = 10;

        float gridWidth = info.Width - LeftMargin - RightMargin;
        float gridHeight = info.Height - TopMargin - BottomMargin;

        float cellWidth = (gridWidth - (yearSpan - 1) * CellPadding) / yearSpan;
        float cellHeight = (gridHeight - (scoreCount - 1) * CellPadding) / scoreCount;

        using var labelFont = new SKFont(ConsolasTypeface, 16);
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(150, 150, 150)
        };

        // X-axis: Year labels (every 5 years)
        for (int yi = 0; yi < yearSpan; yi++)
        {
            int year = _minYear + yi;
            if (year % 5 != 0) continue;

            float x = LeftMargin + yi * (cellWidth + CellPadding) + cellWidth / 2;
            float y = info.Height - 8;

            var text = year.ToString();
            var textWidth = labelFont.MeasureText(text);
            canvas.DrawText(text, x - textWidth / 2, y, labelFont, labelPaint);
        }

        // Y-axis: Score labels (1-10)
        for (int si = 0; si < scoreCount; si++)
        {
            int score = 10 - si;
            float y = TopMargin + si * (cellHeight + CellPadding) + cellHeight / 2 + 5;

            var text = score.ToString();
            var textWidth = labelFont.MeasureText(text);
            canvas.DrawText(text, LeftMargin - textWidth - 8, y, labelFont, labelPaint);
        }

        // Axis titles
        using var titleFont = new SKFont(ConsolasTypeface, 13);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(100, 100, 100)
        };

        // "YEAR" at bottom center
        var yearTitle = "YEAR";
        var yearTitleWidth = titleFont.MeasureText(yearTitle);
        canvas.DrawText(yearTitle, LeftMargin + gridWidth / 2 - yearTitleWidth / 2, info.Height - 28, titleFont, titlePaint);

        // "SCORE" rotated on left
        canvas.Save();
        canvas.RotateDegrees(-90, 16, TopMargin + gridHeight / 2);
        var scoreTitle = "IMPORTANCE SCORE";
        var scoreTitleWidth = titleFont.MeasureText(scoreTitle);
        canvas.DrawText(scoreTitle, 16 - scoreTitleWidth / 2, TopMargin + gridHeight / 2 + 5, titleFont, titlePaint);
        canvas.Restore();
    }

    private void DrawBivariateLegend(SKCanvas canvas, SKImageInfo info)
    {
        // Draw a 5x5 bivariate color key in the right margin
        float legendSize = 110;
        float legendX = info.Width - RightMargin + 25;
        float legendY = TopMargin + 10;
        float cellSize = legendSize / 5;

        // Title
        using var titleFont = new SKFont(ConsolasTypeface, 13);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(150, 150, 150)
        };
        canvas.DrawText("LEGEND", legendX, legendY, titleFont, titlePaint);
        legendY += 18;

        using var cellPaint = new SKPaint { IsAntialias = false };

        for (int tx = 0; tx < 5; tx++)
        {
            for (int uy = 0; uy < 5; uy++)
            {
                double t = tx / 4.0; // tracked: 0→1 left to right
                double u = (4 - uy) / 4.0; // untracked: 0→1 bottom to top

                double sum = t + u;
                double ratio = sum > 0 ? t / sum : 0.5;
                double brightness = Math.Min(1.0, (t + u) / 2.0);
                brightness = 0.15 + brightness * 0.85;

                SKColor baseColor;
                if (ratio >= 0.5)
                {
                    double f = (ratio - 0.5) * 2.0;
                    baseColor = LerpColor(GreenTint, BlueTint, f);
                }
                else
                {
                    double f = ratio * 2.0;
                    baseColor = LerpColor(YellowTint, GreenTint, f);
                }

                cellPaint.Color = (t == 0 && u == 0) ? DarkBg : LerpColor(DarkBg, baseColor, brightness);

                float cx = legendX + tx * cellSize;
                float cy = legendY + uy * cellSize;
                canvas.DrawRect(cx, cy, cellSize - 1, cellSize - 1, cellPaint);
            }
        }

        // Axis labels for legend
        using var labelFont = new SKFont(ConsolasTypeface, 12);
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(120, 120, 120)
        };

        // Bottom label: "Tracked →"
        canvas.DrawText("Tracked \u2192", legendX, legendY + legendSize + 16, labelFont, labelPaint);

        // Left label: "Untracked ↑" (rotated)
        canvas.Save();
        canvas.RotateDegrees(-90, legendX - 8, legendY + legendSize / 2);
        canvas.DrawText("Untracked \u2192", legendX - 8 - 40, legendY + legendSize / 2 + 4, labelFont, labelPaint);
        canvas.Restore();

        // Color key labels
        float keyY = legendY + legendSize + 34;
        using var keyPaint = new SKPaint { IsAntialias = false };
        using var keyLabelFont = new SKFont(ConsolasTypeface, 13);
        using var keyLabelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(150, 150, 150)
        };

        // Blue = tracked
        keyPaint.Color = BlueTint;
        canvas.DrawRect(legendX, keyY, 14, 14, keyPaint);
        canvas.DrawText("Tracked", legendX + 20, keyY + 12, keyLabelFont, keyLabelPaint);

        // Yellow = untracked
        keyPaint.Color = YellowTint;
        canvas.DrawRect(legendX, keyY + 22, 14, 14, keyPaint);
        canvas.DrawText("Untracked", legendX + 20, keyY + 34, keyLabelFont, keyLabelPaint);

        // Green = both
        keyPaint.Color = GreenTint;
        canvas.DrawRect(legendX, keyY + 44, 14, 14, keyPaint);
        canvas.DrawText("Both", legendX + 20, keyY + 56, keyLabelFont, keyLabelPaint);
    }

    private static void DrawCenteredText(SKCanvas canvas, SKImageInfo info, string text, float size, SKColor color)
    {
        using var font = new SKFont(ConsolasTypeface, size);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color
        };
        var textWidth = font.MeasureText(text);
        canvas.DrawText(text, (info.Width - textWidth) / 2, info.Height / 2, font, paint);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_cellLookup == null || _cellLookup.Count == 0) return;

        var pos = e.GetPosition(_skElement);
        var (year, score) = HitTest(pos.X, pos.Y);

        if (year != _hoverYear || score != _hoverScore)
        {
            _hoverYear = year;
            _hoverScore = score;
            _skElement.InvalidateVisual();

            if (year >= 0 && score >= 0 && _cellLookup.TryGetValue((year, score), out var cell))
            {
                _tooltip.Content = $"Year: {year}  Score: {score}\n" +
                    $"Tracked: {cell.TrackedRecords:N0} records ({cell.TrackedSecurities:N0} securities)\n" +
                    $"Untracked: {cell.UntrackedRecords:N0} records ({cell.UntrackedSecurities:N0} securities)";
                _tooltip.IsOpen = true;
            }
            else if (year >= 0 && score >= 0)
            {
                _tooltip.Content = $"Year: {year}  Score: {score}\nNo data";
                _tooltip.IsOpen = true;
            }
            else
            {
                _tooltip.IsOpen = false;
            }
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverYear = -1;
        _hoverScore = -1;
        _tooltip.IsOpen = false;
        _skElement.InvalidateVisual();
    }

    private (int year, int score) HitTest(double mouseX, double mouseY)
    {
        if (_cellLookup == null) return (-1, -1);

        // Convert WPF coordinates to pixel coordinates (account for DPI)
        var source = PresentationSource.FromVisual(_skElement);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        float px = (float)(mouseX * dpiScaleX);
        float py = (float)(mouseY * dpiScaleY);

        int yearSpan = _maxYear - _minYear + 1;
        int scoreCount = 10;

        float actualWidth = (float)(_skElement.ActualWidth * dpiScaleX);
        float actualHeight = (float)(_skElement.ActualHeight * dpiScaleY);

        float gridWidth = actualWidth - LeftMargin - RightMargin;
        float gridHeight = actualHeight - TopMargin - BottomMargin;

        float cellWidth = (gridWidth - (yearSpan - 1) * CellPadding) / yearSpan;
        float cellHeight = (gridHeight - (scoreCount - 1) * CellPadding) / scoreCount;

        // Check bounds
        if (px < LeftMargin || px > LeftMargin + gridWidth ||
            py < TopMargin || py > TopMargin + gridHeight)
            return (-1, -1);

        int yi = (int)((px - LeftMargin) / (cellWidth + CellPadding));
        int si = (int)((py - TopMargin) / (cellHeight + CellPadding));

        yi = Math.Clamp(yi, 0, yearSpan - 1);
        si = Math.Clamp(si, 0, scoreCount - 1);

        return (_minYear + yi, 10 - si);
    }
}
