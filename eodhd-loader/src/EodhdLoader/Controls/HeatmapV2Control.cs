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
/// V2 coverage heatmap: single-axis green gradient with amorphous blob rendering.
/// Black = no data, bright green = full coverage.
/// Intensity = 0.5 × tracked_normalized + 0.5 × untracked_normalized.
/// </summary>
public class HeatmapV2Control : UserControl
{
    public static readonly DependencyProperty HeatmapDataProperty =
        DependencyProperty.Register(nameof(HeatmapData), typeof(HeatmapDataResult), typeof(HeatmapV2Control),
            new PropertyMetadata(null, OnHeatmapDataChanged));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(HeatmapV2Control),
            new PropertyMetadata(false, OnIsLoadingChanged));

    public static readonly DependencyProperty ActiveYearProperty =
        DependencyProperty.Register(nameof(ActiveYear), typeof(int), typeof(HeatmapV2Control),
            new PropertyMetadata(-1, OnActiveCellChanged));

    public static readonly DependencyProperty ActiveScoreProperty =
        DependencyProperty.Register(nameof(ActiveScore), typeof(int), typeof(HeatmapV2Control),
            new PropertyMetadata(-1, OnActiveCellChanged));

    public static readonly DependencyProperty CellUpdateCounterProperty =
        DependencyProperty.Register(nameof(CellUpdateCounter), typeof(long), typeof(HeatmapV2Control),
            new PropertyMetadata(0L, OnCellUpdateCounterChanged));

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

    public long CellUpdateCounter
    {
        get => (long)GetValue(CellUpdateCounterProperty);
        set => SetValue(CellUpdateCounterProperty, value);
    }

    // Colors
    private static readonly SKColor BrightGreen = new(0, 255, 136);  // #00ff88
    private static readonly SKColor DarkBg = new(10, 10, 21);        // #0a0a15
    private static readonly SKTypeface ConsolasTypeface = SKTypeface.FromFamilyName("Consolas");

    // Layout
    private const float LeftMargin = 65f;
    private const float BottomMargin = 55f;
    private const float TopMargin = 10f;
    private const float RightMargin = 120f;
    private const float CellPadding = 1f;

    // Pre-computed data
    private Dictionary<(int year, int score), HeatmapCell>? _cellLookup;
    private int _minYear, _maxYear;
    private long _maxTracked, _maxUntracked;

    // Hover state
    private int _hoverYear = -1;
    private int _hoverScore = -1;

    // Purple→green fade for recently updated cells
    private readonly Dictionary<(int year, int score), DateTime> _recentlyUpdated = new();
    private Dictionary<(int year, int score), (long tracked, long untracked)>? _previousCounts;
    private const double FadeDurationSeconds = 10.0;

    // Ripple animation
    private readonly DispatcherTimer _pulseTimer;
    private double _pulsePhase;
    private const double PulseSpeed = 0.15;
    private const int RippleCount = 3;
    private const double RipplePeriod = 6 * Math.PI;  // ~4.2s per ring cycle
    private const double RippleSpacing = RipplePeriod / RippleCount;

    private readonly SKElement _skElement;
    private readonly ToolTip _tooltip;

    public HeatmapV2Control()
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

        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulsePhase += PulseSpeed;
            if (_pulsePhase > 1000 * Math.PI) _pulsePhase -= 1000 * Math.PI;

            // Clean up expired fades
            if (_recentlyUpdated.Count > 0)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-FadeDurationSeconds);
                var expired = new List<(int, int)>();
                foreach (var kvp in _recentlyUpdated)
                    if (kvp.Value < cutoff) expired.Add(kvp.Key);
                foreach (var key in expired)
                    _recentlyUpdated.Remove(key);

                if (_recentlyUpdated.Count == 0 && ActiveYear < 0 && ActiveScore < 0)
                    _pulseTimer.Stop();
            }

            _skElement.InvalidateVisual();
        };

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;

        Content = _skElement;
    }

    private static void OnHeatmapDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapV2Control control)
        {
            control.PrepareData();
            control._skElement.InvalidateVisual();
        }
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapV2Control control)
            control._skElement.InvalidateVisual();
    }

    private static void OnActiveCellChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapV2Control control)
        {
            bool hasActiveCell = control.ActiveYear >= 0 && control.ActiveScore >= 0;
            if (hasActiveCell && !control._pulseTimer.IsEnabled)
            {
                control._pulsePhase = 0;
                control._pulseTimer.Start();
            }
            else if (!hasActiveCell && control._pulseTimer.IsEnabled)
            {
                if (control._recentlyUpdated.Count == 0)
                    control._pulseTimer.Stop();
                control._skElement.InvalidateVisual();
            }
        }
    }

    private static void OnCellUpdateCounterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapV2Control control && control.ActiveYear >= 0 && control.ActiveScore >= 0)
        {
            var key = (control.ActiveYear, control.ActiveScore);
            control._recentlyUpdated[key] = DateTime.UtcNow;

            if (!control._pulseTimer.IsEnabled)
            {
                control._pulsePhase = 0;
                control._pulseTimer.Start();
            }

            control._skElement.InvalidateVisual();
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

        var newLookup = new Dictionary<(int, int), HeatmapCell>();
        foreach (var cell in data.Cells)
        {
            newLookup[(cell.Year, cell.Score)] = cell;
        }

        // Detect cells with increased record counts → mark for purple fade
        if (_previousCounts != null)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in newLookup)
            {
                var key = kvp.Key;
                var cell = kvp.Value;
                if (_previousCounts.TryGetValue(key, out var prev))
                {
                    if (cell.TrackedRecords > prev.tracked || cell.UntrackedRecords > prev.untracked)
                        _recentlyUpdated[key] = now;
                }
                else if (cell.TrackedRecords > 0 || cell.UntrackedRecords > 0)
                {
                    _recentlyUpdated[key] = now;
                }
            }

            // Start timer for fade animation if needed
            if (_recentlyUpdated.Count > 0 && !_pulseTimer.IsEnabled)
                _pulseTimer.Start();
        }

        // Store current counts for next comparison
        _previousCounts = new Dictionary<(int, int), (long, long)>();
        foreach (var kvp in newLookup)
            _previousCounts[kvp.Key] = (kvp.Value.TrackedRecords, kvp.Value.UntrackedRecords);

        _cellLookup = newLookup;
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
            DrawCenteredText(canvas, info, "Click Refresh to load heatmap data", 14, new SKColor(136, 136, 136));
            return;
        }

        DrawBlobs(canvas, info);
        DrawActiveAndHoverOverlays(canvas, info);
        DrawAxisLabels(canvas, info);
        DrawLegend(canvas, info);
    }

    private void DrawBlobs(SKCanvas canvas, SKImageInfo info)
    {
        if (_cellLookup == null) return;

        int yearSpan = _maxYear - _minYear + 1;
        int scoreCount = 10;

        float gridWidth = info.Width - LeftMargin - RightMargin;
        float gridHeight = info.Height - TopMargin - BottomMargin;

        float cellWidth = (gridWidth - (yearSpan - 1) * CellPadding) / yearSpan;
        float cellHeight = (gridHeight - (scoreCount - 1) * CellPadding) / scoreCount;

        // Blob radius: larger overlap for solid fill in dense areas
        float blobRadius = Math.Max(cellWidth, cellHeight) * 1.6f;

        // Blur sigma proportional to cell size for smooth appearance
        float blurSigma = Math.Max(cellWidth, cellHeight) * 0.5f;

        // Create off-screen surface for blob rendering with Screen blend mode
        using var offscreenSurface = SKSurface.Create(info);
        if (offscreenSurface == null) return;

        var offCanvas = offscreenSurface.Canvas;
        offCanvas.Clear(SKColors.Black);

        // Extend clip beyond grid to allow edge phantom blobs to render
        offCanvas.Save();
        offCanvas.ClipRect(new SKRect(LeftMargin - blobRadius, TopMargin - blobRadius,
            info.Width - RightMargin + blobRadius, info.Height - BottomMargin + blobRadius));

        // Color state for current blob (captured by DrawBlob closure)
        byte blobR = 0, blobG = 255, blobB = 136;

        // Local function to draw a single blob at a position
        void DrawBlob(float bx, float by, double intensity)
        {
            byte r = (byte)(blobR * intensity);
            byte g = (byte)(blobG * intensity);
            byte b = (byte)(blobB * intensity);
            byte a = (byte)(245 * intensity);

            // Hold color longer before fading — solid center, gradual fade at edge
            var colors = new[]
            {
                new SKColor(r, g, b, a),
                new SKColor(r, g, b, (byte)(a * 0.7)),
                new SKColor(r, g, b, (byte)(a * 0.2)),
                SKColors.Transparent
            };
            var positions = new[] { 0f, 0.45f, 0.75f, 1f };

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(bx, by), blobRadius,
                colors, positions, SKShaderTileMode.Clamp);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Shader = shader,
                BlendMode = SKBlendMode.Screen
            };

            offCanvas.DrawCircle(bx, by, blobRadius, paint);
        }

        // Draw radial gradient blob for each data point
        float cellStepX = cellWidth + CellPadding;
        float cellStepY = cellHeight + CellPadding;

        foreach (var kvp in _cellLookup)
        {
            var (year, score) = kvp.Key;
            var cell = kvp.Value;

            if (cell.TrackedRecords == 0 && cell.UntrackedRecords == 0)
                continue;

            // Determine color: purple→green fade for recently updated cells
            if (_recentlyUpdated.TryGetValue((year, score), out var updatedAt))
            {
                double elapsed = (DateTime.UtcNow - updatedAt).TotalSeconds;
                double fadeT = Math.Clamp(elapsed / FadeDurationSeconds, 0, 1);
                // Soft purple (130, 50, 255) → green (0, 255, 136)
                blobR = (byte)(130 * (1 - fadeT));
                blobG = (byte)(50 + 205 * fadeT);
                blobB = (byte)(255 + (136 - 255) * fadeT);
            }
            else
            {
                blobR = 0; blobG = 255; blobB = 136;
            }

            // Normalize with sqrt perceptual scaling
            double t = Math.Sqrt(Math.Min(1.0, (double)cell.TrackedRecords / _maxTracked));
            double u = Math.Sqrt(Math.Min(1.0, (double)cell.UntrackedRecords / _maxUntracked));
            double intensity = 0.5 * t + 0.5 * u;

            if (intensity < 0.01) continue;

            int yi = year - _minYear;
            int si = 10 - score; // Score 10 at top

            float cx = LeftMargin + yi * cellStepX + cellWidth / 2;
            float cy = TopMargin + si * cellStepY + cellHeight / 2;

            // Draw the real blob
            DrawBlob(cx, cy, intensity);

            // Mirror phantom blobs for cells near edges (2 deep) to prevent fade at boundaries
            int phantomDepth = 2;

            for (int d = 1; d <= phantomDepth; d++)
            {
                if (yi < phantomDepth) DrawBlob(cx - d * cellStepX, cy, intensity);
                if (yi >= yearSpan - phantomDepth) DrawBlob(cx + d * cellStepX, cy, intensity);
                if (si < phantomDepth) DrawBlob(cx, cy - d * cellStepY, intensity);
                if (si >= scoreCount - phantomDepth) DrawBlob(cx, cy + d * cellStepY, intensity);
            }

            // Corner phantoms for cells near both edges
            if (yi < phantomDepth && si < phantomDepth)
                for (int dx = 1; dx <= phantomDepth; dx++)
                    for (int dy = 1; dy <= phantomDepth; dy++)
                        DrawBlob(cx - dx * cellStepX, cy - dy * cellStepY, intensity);
            if (yi >= yearSpan - phantomDepth && si < phantomDepth)
                for (int dx = 1; dx <= phantomDepth; dx++)
                    for (int dy = 1; dy <= phantomDepth; dy++)
                        DrawBlob(cx + dx * cellStepX, cy - dy * cellStepY, intensity);
            if (yi < phantomDepth && si >= scoreCount - phantomDepth)
                for (int dx = 1; dx <= phantomDepth; dx++)
                    for (int dy = 1; dy <= phantomDepth; dy++)
                        DrawBlob(cx - dx * cellStepX, cy + dy * cellStepY, intensity);
            if (yi >= yearSpan - phantomDepth && si >= scoreCount - phantomDepth)
                for (int dx = 1; dx <= phantomDepth; dx++)
                    for (int dy = 1; dy <= phantomDepth; dy++)
                        DrawBlob(cx + dx * cellStepX, cy + dy * cellStepY, intensity);
        }

        offCanvas.Restore();

        // Snapshot the off-screen render
        using var snapshot = offscreenSurface.Snapshot();

        // Draw blurred blobs onto main canvas
        using var blurFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma);
        using var blurPaint = new SKPaint { ImageFilter = blurFilter };

        // Clip main canvas to grid area for the blurred render
        canvas.Save();
        canvas.ClipRect(new SKRect(LeftMargin, TopMargin,
            info.Width - RightMargin, info.Height - BottomMargin));
        canvas.DrawImage(snapshot, 0, 0, blurPaint);
        canvas.Restore();
    }

    private void DrawActiveAndHoverOverlays(SKCanvas canvas, SKImageInfo info)
    {
        if (_cellLookup == null) return;

        int yearSpan = _maxYear - _minYear + 1;
        int scoreCount = 10;

        float gridWidth = info.Width - LeftMargin - RightMargin;
        float gridHeight = info.Height - TopMargin - BottomMargin;

        float cellWidth = (gridWidth - (yearSpan - 1) * CellPadding) / yearSpan;
        float cellHeight = (gridHeight - (scoreCount - 1) * CellPadding) / scoreCount;

        // Active cell: stone-drop ripple effect
        if (ActiveYear >= _minYear && ActiveYear <= _maxYear && ActiveScore >= 1 && ActiveScore <= 10)
        {
            int yi = ActiveYear - _minYear;
            int si = 10 - ActiveScore;

            float x = LeftMargin + yi * (cellWidth + CellPadding);
            float y = TopMargin + si * (cellHeight + CellPadding);
            float cx = x + cellWidth / 2;
            float cy = y + cellHeight / 2;

            // Compute max ripple radius: distance from epicenter to farthest grid corner
            float gridLeft = LeftMargin;
            float gridTop = TopMargin;
            float gridRight = info.Width - RightMargin;
            float gridBottom = info.Height - BottomMargin;

            float maxDx = Math.Max(cx - gridLeft, gridRight - cx);
            float maxDy = Math.Max(cy - gridTop, gridBottom - cy);
            float maxRippleRadius = (float)Math.Sqrt(maxDx * maxDx + maxDy * maxDy);

            // Clip ripple rings to grid area
            canvas.Save();
            canvas.ClipRect(new SKRect(gridLeft, gridTop, gridRight, gridBottom));

            // Draw expanding ripple rings
            for (int i = 0; i < RippleCount; i++)
            {
                double ringPhase = (_pulsePhase + i * RippleSpacing) % RipplePeriod;
                double t = ringPhase / RipplePeriod; // 0 to 1

                float radius = (float)(t * maxRippleRadius);
                float fade = (float)((1.0 - t) * (1.0 - t)); // quadratic fade
                byte alpha = (byte)(fade * 180);
                float strokeWidth = 2.5f * (1.0f - (float)t) + 0.5f;

                if (alpha < 2) continue;

                using var ringPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(0, 255, 170, alpha),
                    StrokeWidth = strokeWidth
                };
                canvas.DrawCircle(cx, cy, radius, ringPaint);
            }

            canvas.Restore();

            // Epicenter dot (drawn on top, outside clip)
            double pulse = (Math.Sin(_pulsePhase * 3) + 1.0) / 2.0; // faster pulse for small dot
            byte dotAlpha = (byte)(120 + pulse * 135);
            float dotRadius = Math.Min(cellWidth, cellHeight) * 0.2f;

            using var dotPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 255, 136, dotAlpha)
            };
            canvas.DrawCircle(cx, cy, dotRadius, dotPaint);
        }

        // Hover highlight
        if (_hoverYear >= _minYear && _hoverYear <= _maxYear && _hoverScore >= 1 && _hoverScore <= 10
            && (_hoverYear != ActiveYear || _hoverScore != ActiveScore))
        {
            int yi = _hoverYear - _minYear;
            int si = 10 - _hoverScore;

            float x = LeftMargin + yi * (cellWidth + CellPadding);
            float y = TopMargin + si * (cellHeight + CellPadding);

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

        var yearTitle = "YEAR";
        var yearTitleWidth = titleFont.MeasureText(yearTitle);
        canvas.DrawText(yearTitle, LeftMargin + gridWidth / 2 - yearTitleWidth / 2, info.Height - 28, titleFont, titlePaint);

        canvas.Save();
        canvas.RotateDegrees(-90, 16, TopMargin + gridHeight / 2);
        var scoreTitle = "IMPORTANCE SCORE";
        var scoreTitleWidth = titleFont.MeasureText(scoreTitle);
        canvas.DrawText(scoreTitle, 16 - scoreTitleWidth / 2, TopMargin + gridHeight / 2 + 5, titleFont, titlePaint);
        canvas.Restore();
    }

    private void DrawLegend(SKCanvas canvas, SKImageInfo info)
    {
        float legendX = info.Width - RightMargin + 15;
        float legendY = TopMargin + 10;

        // Title
        using var titleFont = new SKFont(ConsolasTypeface, 13);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(150, 150, 150)
        };
        canvas.DrawText("COVERAGE", legendX, legendY, titleFont, titlePaint);
        legendY += 22;

        // Vertical gradient bar: black at bottom → green at top
        float barWidth = 20;
        float barHeight = info.Height - TopMargin - BottomMargin - 80;

        using var gradientPaint = new SKPaint { IsAntialias = true };
        var gradientColors = new[]
        {
            BrightGreen,
            new SKColor(0, 180, 96),
            new SKColor(0, 80, 42),
            DarkBg
        };
        var gradientPositions = new[] { 0f, 0.3f, 0.7f, 1f };

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(legendX, legendY),
            new SKPoint(legendX, legendY + barHeight),
            gradientColors, gradientPositions,
            SKShaderTileMode.Clamp);
        gradientPaint.Shader = shader;

        canvas.DrawRoundRect(legendX, legendY, barWidth, barHeight, 3, 3, gradientPaint);

        // Border around gradient bar
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 60, 80),
            StrokeWidth = 1
        };
        canvas.DrawRoundRect(legendX, legendY, barWidth, barHeight, 3, 3, borderPaint);

        // Labels
        using var labelFont = new SKFont(ConsolasTypeface, 11);
        using var labelPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(120, 120, 120)
        };

        canvas.DrawText("Full", legendX + barWidth + 6, legendY + 10, labelFont, labelPaint);
        canvas.DrawText("None", legendX + barWidth + 6, legendY + barHeight, labelFont, labelPaint);
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
                double t = Math.Sqrt(Math.Min(1.0, (double)cell.TrackedRecords / _maxTracked));
                double u = Math.Sqrt(Math.Min(1.0, (double)cell.UntrackedRecords / _maxUntracked));
                double intensity = 0.5 * t + 0.5 * u;

                _tooltip.Content = $"Year: {year}  Score: {score}\n" +
                    $"Tracked: {cell.TrackedRecords:N0} records ({cell.TrackedSecurities:N0} securities)\n" +
                    $"Untracked: {cell.UntrackedRecords:N0} records ({cell.UntrackedSecurities:N0} securities)\n" +
                    $"Coverage: {intensity:P0}";
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
