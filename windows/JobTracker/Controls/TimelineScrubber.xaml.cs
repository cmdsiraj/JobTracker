// Interactive timeline range control: a per-month activity histogram with a
// draggable selection window (two handles + translatable middle). Snaps to
// month boundaries.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JobTracker.ViewModels;

namespace JobTracker.Controls;

public partial class TimelineScrubber : UserControl
{
    private const double HandleWidth = 10;
    private const double BarAreaHeight = 36;

    private DateRange _domain = new(DateTimeOffset.Now.AddMonths(-5), DateTimeOffset.Now);
    private List<(DateTimeOffset Month, int Count)> _histogram = [];
    private DateRange _selection;

    private enum DragMode { None, Lower, Upper, Window }
    private DragMode _dragMode = DragMode.None;
    private DateRange _dragStartSelection;

    public event Action<DateRange>? SelectionChanged;

    public TimelineScrubber()
    {
        InitializeComponent();
        _selection = _domain;
    }

    public void Update(DateRange domain, List<(DateTimeOffset Month, int Count)> histogram, DateRange selection)
    {
        _domain = domain;
        _histogram = histogram;
        _selection = selection;
        Render();
    }

    private void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        TrackCanvas.Children.Clear();
        var width = TrackCanvas.ActualWidth;
        if (width <= 0) { UpdateLabels(); return; }

        var maxCount = Math.Max(1, _histogram.Count > 0 ? _histogram.Max(h => h.Count) : 1);
        var barSlot = _histogram.Count > 0 ? width / _histogram.Count : width;
        for (var i = 0; i < _histogram.Count; i++)
        {
            var (month, count) = _histogram[i];
            var inSelection = month >= _selection.Start && month <= _selection.End;
            var barHeight = count == 0 ? 2 : Math.Max(4, BarAreaHeight * count / (double)maxCount);
            var bar = new Rectangle
            {
                Width = Math.Max(1, barSlot - 2),
                Height = barHeight,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = inSelection
                    ? new SolidColorBrush(Color.FromArgb(140, 0x35, 0x7A, 0xBD))
                    : new SolidColorBrush(Color.FromArgb(90, 0x6B, 0x6B, 0x72)),
            };
            Canvas.SetLeft(bar, i * barSlot + 1);
            Canvas.SetTop(bar, BarAreaHeight - barHeight);
            TrackCanvas.Children.Add(bar);
        }

        var lowerX = XFor(_selection.Start, width);
        var upperX = XFor(_selection.End, width);

        AddDim(0, Math.Max(0, lowerX));
        AddDim(upperX, Math.Max(0, width - upperX));

        var accent = (Brush)FindResource("AccentBrush");
        var windowRect = new Rectangle
        {
            Width = Math.Max(HandleWidth, upperX - lowerX),
            Height = BarAreaHeight,
            RadiusX = 6,
            RadiusY = 6,
            Fill = new SolidColorBrush(Color.FromArgb(20, 0x35, 0x7A, 0xBD)),
            Stroke = accent,
            StrokeThickness = 1.5,
        };
        Canvas.SetLeft(windowRect, lowerX);
        Canvas.SetTop(windowRect, 0);
        TrackCanvas.Children.Add(windowRect);

        AddHandle(lowerX, accent);
        AddHandle(upperX, accent);

        UpdateLabels();
    }

    private void AddDim(double x, double w)
    {
        if (w <= 0) return;
        var rect = new Rectangle
        {
            Width = w,
            Height = BarAreaHeight,
            Fill = new SolidColorBrush(Color.FromArgb(140, 0xF7, 0xF7, 0xF8)),
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, 0);
        TrackCanvas.Children.Add(rect);
    }

    private void AddHandle(double x, Brush accent)
    {
        var handle = new Rectangle
        {
            Width = HandleWidth,
            Height = BarAreaHeight + 6,
            RadiusX = 5,
            RadiusY = 5,
            Fill = accent,
        };
        Canvas.SetLeft(handle, x - HandleWidth / 2);
        Canvas.SetTop(handle, -3);
        TrackCanvas.Children.Add(handle);
    }

    private void UpdateLabels()
    {
        RangeLabel.Text = $"{MonthLabel(_selection.Start)} – {MonthLabel(EndLabelDate())}";
        DomainStartLabel.Text = MonthLabel(_domain.Start);
        DomainEndLabel.Text = MonthLabel(_domain.End);
    }

    private DateTimeOffset EndLabelDate()
    {
        var lastIncluded = _selection.End.AddMonths(-1);
        return lastIncluded > _selection.Start ? lastIncluded : _selection.Start;
    }

    private static string MonthLabel(DateTimeOffset date) => date.ToString("MMM yyyy");

    // MARK: - Position <-> date

    private double XFor(DateTimeOffset date, double width)
    {
        var total = (_domain.End - _domain.Start).TotalSeconds;
        if (total <= 0) return 0;
        var fraction = (date - _domain.Start).TotalSeconds / total;
        return width * Math.Clamp(fraction, 0, 1);
    }

    private DateTimeOffset SnappedDate(double x, double width)
    {
        var total = (_domain.End - _domain.Start).TotalSeconds;
        var fraction = Math.Clamp(x / Math.Max(width, 1), 0, 1);
        var raw = _domain.Start.AddSeconds(fraction * total);
        var start = TimelineMath.StartOfMonth(raw);
        var next = start.AddMonths(1);
        return (raw - start) < (next - raw) ? start : next;
    }

    // MARK: - Mouse interaction

    private void TrackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var width = TrackCanvas.ActualWidth;
        var x = e.GetPosition(TrackCanvas).X;
        var lowerX = XFor(_selection.Start, width);
        var upperX = XFor(_selection.End, width);

        if (Math.Abs(x - lowerX) <= 10) _dragMode = DragMode.Lower;
        else if (Math.Abs(x - upperX) <= 10) _dragMode = DragMode.Upper;
        else if (x > lowerX && x < upperX) _dragMode = DragMode.Window;
        else return;

        _dragStartSelection = _selection;
        TrackCanvas.CaptureMouse();
    }

    private void TrackCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == DragMode.None) return;
        var width = TrackCanvas.ActualWidth;
        var x = e.GetPosition(TrackCanvas).X;
        var date = SnappedDate(x, width);

        switch (_dragMode)
        {
            case DragMode.Lower:
            {
                var maxLower = _dragStartSelection.End.AddMonths(-1);
                var lower = Clamp(date, _domain.Start, maxLower);
                _selection = new DateRange(lower, _selection.End);
                break;
            }
            case DragMode.Upper:
            {
                var minUpper = _dragStartSelection.Start.AddMonths(1);
                var upper = Clamp(date, minUpper, _domain.End);
                _selection = new DateRange(_selection.Start, upper);
                break;
            }
            case DragMode.Window:
            {
                var monthsSpan = MonthsBetween(_dragStartSelection.Start, _dragStartSelection.End);
                var lower = date;
                var upper = lower.AddMonths(monthsSpan);
                if (upper > _domain.End) { upper = _domain.End; lower = upper.AddMonths(-monthsSpan); }
                if (lower < _domain.Start) { lower = _domain.Start; upper = lower.AddMonths(monthsSpan); }
                _selection = new DateRange(lower, upper > lower ? upper : lower);
                break;
            }
        }
        Render();
    }

    private void TrackCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.None) return;
        _dragMode = DragMode.None;
        TrackCanvas.ReleaseMouseCapture();
        SelectionChanged?.Invoke(_selection);
    }

    private void ResetToDefault_Click(object sender, RoutedEventArgs e)
    {
        _selection = TimelineMath.DefaultWindow(_domain);
        Render();
        SelectionChanged?.Invoke(_selection);
    }

    private static DateTimeOffset Clamp(DateTimeOffset value, DateTimeOffset min, DateTimeOffset max)
    {
        if (min > max) (min, max) = (max, min);
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static int MonthsBetween(DateTimeOffset a, DateTimeOffset b) =>
        (b.Year - a.Year) * 12 + (b.Month - a.Month);
}
