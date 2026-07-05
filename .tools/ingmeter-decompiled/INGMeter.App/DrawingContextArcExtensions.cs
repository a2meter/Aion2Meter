using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

internal static class DrawingContextArcExtensions
{
	public static void DrawArc(this DrawingContext dc, Pen pen, Point start, Point end, double radiusX, double radiusY, bool isLargeArc)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(start, isFilled: false, isClosed: false);
			streamGeometryContext.ArcTo(end, new Size(radiusX, radiusY), 0.0, isLargeArc, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(null, pen, streamGeometry);
	}
}
