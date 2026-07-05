using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BuffTimerRing : FrameworkElement
{
	public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(BuffTimerRing), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register("RingBrush", typeof(Brush), typeof(BuffTimerRing), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register("TrackBrush", typeof(Brush), typeof(BuffTimerRing), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register("StrokeThickness", typeof(double), typeof(BuffTimerRing), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public double Progress
	{
		get
		{
			return (double)GetValue(ProgressProperty);
		}
		set
		{
			SetValue(ProgressProperty, value);
		}
	}

	public Brush RingBrush
	{
		get
		{
			return (Brush)GetValue(RingBrushProperty);
		}
		set
		{
			SetValue(RingBrushProperty, value);
		}
	}

	public Brush TrackBrush
	{
		get
		{
			return (Brush)GetValue(TrackBrushProperty);
		}
		set
		{
			SetValue(TrackBrushProperty, value);
		}
	}

	public double StrokeThickness
	{
		get
		{
			return (double)GetValue(StrokeThicknessProperty);
		}
		set
		{
			SetValue(StrokeThicknessProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double max = Math.Max(1.0, Math.Min(base.ActualWidth, base.ActualHeight) / 2.0);
		double num = Math.Clamp(StrokeThickness, 1.0, max);
		double num2 = Math.Max(0.0, Math.Min(base.ActualWidth, base.ActualHeight) / 2.0 - num / 2.0);
		if (num2 <= 0.0)
		{
			return;
		}
		Point center = new Point(base.ActualWidth / 2.0, base.ActualHeight / 2.0);
		dc.DrawEllipse(null, new Pen(TrackBrush, num), center, num2, num2);
		double num3 = Math.Clamp(Progress, 0.0, 1.0);
		if (!(num3 <= 0.0))
		{
			Pen pen = new Pen(RingBrush, num)
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			if (num3 >= 0.999)
			{
				dc.DrawEllipse(null, pen, center, num2, num2);
				return;
			}
			double num4 = -90.0;
			double angleDegrees = num4 + 360.0 * num3;
			PathFigure pathFigure = new PathFigure
			{
				StartPoint = PointAt(center, num2, num4),
				IsFilled = false,
				IsClosed = false
			};
			pathFigure.Segments.Add(new ArcSegment(PointAt(center, num2, angleDegrees), new Size(num2, num2), 0.0, num3 > 0.5, SweepDirection.Clockwise, isStroked: true));
			PathGeometry pathGeometry = new PathGeometry();
			pathGeometry.Figures.Add(pathFigure);
			dc.DrawGeometry(null, pen, pathGeometry);
		}
	}

	private static Point PointAt(Point center, double radius, double angleDegrees)
	{
		double num = angleDegrees * Math.PI / 180.0;
		return new Point(center.X + Math.Cos(num) * radius, center.Y + Math.Sin(num) * radius);
	}
}
