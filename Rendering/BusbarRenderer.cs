using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BusbarCAD.Models;

namespace BusbarCAD.Rendering
{
    /// <summary>
    /// Handles rendering of busbars on a canvas.
    /// Separates visual concerns from the data model.
    /// </summary>
    public class BusbarRenderer
    {
        private readonly Canvas _canvas;
        private readonly MaterialSettings _materialSettings;

        public BusbarRenderer(Canvas canvas, MaterialSettings materialSettings)
        {
            _canvas = canvas;
            _materialSettings = materialSettings;
        }

        /// <summary>
        /// Draws a single busbar on the canvas, storing visual references in the busbar object
        /// </summary>
        public void DrawBusbar(Busbar busbar, bool isHighlighted = false)
        {
            if (busbar.Segments.Count == 0) return;

            double thickness = _materialSettings.Thickness;
            double toolRadius = _materialSettings.BendToolRadius;
            double bendRadius = toolRadius + (thickness / 2.0);
            double halfWidth = thickness / 2.0;

            // Get all points from segments
            List<Point2D> points = GetPointsFromSegments(busbar.Segments);
            if (points.Count < 2) return;

            // Draw start marker (blue perpendicular line)
            var firstPoint = points[0];
            var secondPoint = points[1];
            double startAngle = Math.Atan2(secondPoint.Y - firstPoint.Y, secondPoint.X - firstPoint.X);
            busbar.StartMarker = DrawPerpendicularMarker(firstPoint, startAngle, true, isHighlighted);
            busbar.VisualShapes.Add(busbar.StartMarker);

            // Draw centerlines and edge lines for each segment
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                Point2D lineStart = p1;
                Point2D lineEnd = p2;

                // Trim start if there's a previous segment
                if (i > 0)
                {
                    var p0 = points[i - 1];
                    lineStart = TrimLineStart(p0, p1, p2, bendRadius);
                }

                // Trim end if there's a next segment
                if (i < points.Count - 2)
                {
                    var p3 = points[i + 2];
                    lineEnd = TrimLineEnd(p1, p2, p3, bendRadius);
                }

                // Draw centerline (disabled for now)
                // var centerLine = new Line
                // {
                //     X1 = lineStart.X,
                //     Y1 = lineStart.Y,
                //     X2 = lineEnd.X,
                //     Y2 = lineEnd.Y,
                //     Stroke = Brushes.LightGray,
                //     StrokeThickness = 1,
                //     IsHitTestVisible = false
                // };
                // _canvas.Children.Add(centerLine);
                // busbar.VisualShapes.Add(centerLine);

                // Draw edge lines
                DrawEdgeLines(busbar, lineStart, lineEnd, halfWidth);

                // Draw bend arc if there's a next segment
                if (i < points.Count - 2)
                {
                    var p3 = points[i + 2];
                    DrawBendArc(busbar, p1, p2, p3, bendRadius, halfWidth);
                }

                // Draw length label for this segment
                DrawSegmentLengthLabel(busbar, lineStart, lineEnd, i, thickness);
            }

            // Draw end marker (blue perpendicular line)
            var lastPoint = points[points.Count - 1];
            var secondLastPoint = points[points.Count - 2];
            double endAngle = Math.Atan2(lastPoint.Y - secondLastPoint.Y, lastPoint.X - secondLastPoint.X);
            busbar.EndMarker = DrawPerpendicularMarker(lastPoint, endAngle, true, isHighlighted);
            busbar.VisualShapes.Add(busbar.EndMarker);
        }

        /// <summary>
        /// Removes all visual shapes for a busbar from the canvas
        /// </summary>
        public void ClearBusbarVisuals(Busbar busbar)
        {
            foreach (var shape in busbar.VisualShapes)
            {
                _canvas.Children.Remove(shape);
            }
            busbar.ClearVisuals();
        }

        /// <summary>
        /// Redraws all busbars in the layer
        /// </summary>
        public void RedrawAllBusbars(Layer layer, Busbar? highlightedBusbar = null)
        {
            // Clear all busbar visuals first
            foreach (var busbar in layer.Busbars)
            {
                ClearBusbarVisuals(busbar);
            }

            // Redraw all busbars
            foreach (var busbar in layer.Busbars)
            {
                bool isHighlighted = (busbar == highlightedBusbar);
                DrawBusbar(busbar, isHighlighted);
            }
        }

        /// <summary>
        /// Highlights a busbar by making its markers thicker and blue
        /// </summary>
        public void HighlightBusbar(Busbar? previouslyHighlighted, Busbar? newHighlighted)
        {
            // Unhighlight previous (thin black, matching edge lines)
            if (previouslyHighlighted != null)
            {
                if (previouslyHighlighted.StartMarker != null)
                {
                    previouslyHighlighted.StartMarker.StrokeThickness = 0.5;
                    previouslyHighlighted.StartMarker.Stroke = Brushes.Black;
                }
                if (previouslyHighlighted.EndMarker != null)
                {
                    previouslyHighlighted.EndMarker.StrokeThickness = 0.5;
                    previouslyHighlighted.EndMarker.Stroke = Brushes.Black;
                }
            }

            // Highlight new (thick blue)
            if (newHighlighted != null)
            {
                if (newHighlighted.StartMarker != null)
                {
                    newHighlighted.StartMarker.StrokeThickness = 2;
                    newHighlighted.StartMarker.Stroke = Brushes.Blue;
                }
                if (newHighlighted.EndMarker != null)
                {
                    newHighlighted.EndMarker.StrokeThickness = 2;
                    newHighlighted.EndMarker.Stroke = Brushes.Blue;
                }
            }
        }

        #region Helper Methods

        private List<Point2D> GetPointsFromSegments(List<Segment> segments)
        {
            var points = new List<Point2D>();
            if (segments.Count == 0) return points;

            points.Add(segments[0].StartPoint);
            foreach (var segment in segments)
            {
                points.Add(segment.EndPoint);
            }
            return points;
        }

        private Line DrawPerpendicularMarker(Point2D position, double directionAngle, bool isEndPoint, bool isHighlighted)
        {
            double perpAngle = directionAngle + Math.PI / 2.0;
            const double extension = 5.0;

            double cos = Math.Cos(perpAngle);
            double sin = Math.Sin(perpAngle);

            Point2D lineStart = new Point2D(
                position.X - extension * cos,
                position.Y - extension * sin
            );

            Point2D lineEnd = new Point2D(
                position.X + extension * cos,
                position.Y + extension * sin
            );

            var line = new Line
            {
                X1 = lineStart.X,
                Y1 = lineStart.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                Stroke = isHighlighted ? Brushes.Blue : Brushes.Black,
                StrokeThickness = isHighlighted ? 2 : 0.5,
                IsHitTestVisible = false
            };

            _canvas.Children.Add(line);
            return line;
        }

        private void DrawSegmentLengthLabel(Busbar busbar, Point2D start, Point2D end, int segmentIndex, double busbarThickness)
        {
            var segment = busbar.Segments[segmentIndex];
            double length = segment.Length;
            double bendAngle = segment.BendAngle;

            // Calculate segment center point
            double centerX = (start.X + end.X) / 2.0;
            double centerY = (start.Y + end.Y) / 2.0;

            // Calculate segment angle in degrees
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angleRadians = Math.Atan2(dy, dx);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            // Apply small perpendicular offset to move text "up" by 1mm
            double segmentLength = Math.Sqrt(dx * dx + dy * dy);
            if (segmentLength > 0.1)
            {
                double perpX = dy / segmentLength;
                double perpY = -dx / segmentLength;
                double offsetDistance = 1.0; // 1mm upward offset
                centerX += perpX * offsetDistance;
                centerY += perpY * offsetDistance;
            }

            // Normalize angle to -90 to +90 range for text readability
            // (so text doesn't appear upside down)
            if (angleDegrees > 90)
                angleDegrees -= 180;
            else if (angleDegrees < -90)
                angleDegrees += 180;

            // Font size is 80% of busbar thickness
            double fontSize = busbarThickness * 0.8;

            // Format text based on segment position
            string text;
            if (segmentIndex == 0)
            {
                // First segment shows cut length and segment length
                double cutLength = busbar.CalculateCutLength();
                text = $"{cutLength:F1} | {length:F1}";
            }
            else if (Math.Abs(bendAngle) != 90)
            {
                // Other segments show bend angle and length (skip if angle is 90 or -90)
                text = $"{bendAngle:F1}° / {length:F1}";
            }
            else
            {
                // If angle is 90 or -90, just show length
                text = $"{length:F1}";
            }

            // Create text label
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Normal,
                IsHitTestVisible = false
            };

            // Measure the text to get its actual width and height
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                fontSize,
                Brushes.Black,
                1.0);

            double textWidth = formattedText.Width;
            double textHeight = formattedText.Height;

            // Position text so its center is at the segment center
            // Offset by half width and half height
            Canvas.SetLeft(textBlock, centerX - textWidth / 2.0);
            Canvas.SetTop(textBlock, centerY - textHeight / 2.0);

            // Rotate around the text's own center (0.5, 0.5 in relative coordinates)
            textBlock.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            textBlock.RenderTransform = new RotateTransform(angleDegrees);

            _canvas.Children.Add(textBlock);
            busbar.VisualShapes.Add(textBlock);
        }

        private void DrawEdgeLines(Busbar busbar, Point2D start, Point2D end, double halfWidth)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.1) return;

            // Perpendicular direction (normalized)
            double perpX = -dy / length;
            double perpY = dx / length;

            // Offset points for first edge line
            Point2D edge1Start = new Point2D(start.X + perpX * halfWidth, start.Y + perpY * halfWidth);
            Point2D edge1End = new Point2D(end.X + perpX * halfWidth, end.Y + perpY * halfWidth);

            // Offset points for second edge line
            Point2D edge2Start = new Point2D(start.X - perpX * halfWidth, start.Y - perpY * halfWidth);
            Point2D edge2End = new Point2D(end.X - perpX * halfWidth, end.Y - perpY * halfWidth);

            // Draw first edge line
            var line1 = new Line
            {
                X1 = edge1Start.X,
                Y1 = edge1Start.Y,
                X2 = edge1End.X,
                Y2 = edge1End.Y,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(line1);
            busbar.VisualShapes.Add(line1);

            // Draw second edge line
            var line2 = new Line
            {
                X1 = edge2Start.X,
                Y1 = edge2Start.Y,
                X2 = edge2End.X,
                Y2 = edge2End.Y,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            _canvas.Children.Add(line2);
            busbar.VisualShapes.Add(line2);
        }

        private void DrawBendArc(Busbar busbar, Point2D p1, Point2D p2, Point2D p3, double bendRadius, double halfWidth)
        {
            // Calculate the arc that connects the two line segments
            // p1 -> p2 is the previous segment
            // p2 -> p3 is the next segment

            // Direction vectors
            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

            double dx2 = p3.X - p2.X;
            double dy2 = p3.Y - p2.Y;
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (len1 < 0.01 || len2 < 0.01) return;

            // Unit vectors
            double u1x = dx1 / len1;
            double u1y = dy1 / len1;
            double u2x = dx2 / len2;
            double u2y = dy2 / len2;

            // Calculate the angle between segments using dot product
            double dot = u1x * u2x + u1y * u2y;
            double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));

            if (Math.Abs(angleRad) < 0.001) return; // Nearly straight, no arc needed

            // Tangent length for the fillet
            double trimDistance = bendRadius * Math.Tan(angleRad / 2.0);

            // Arc start point (end of trimmed first segment)
            Point2D arcStart = new Point2D(p2.X - u1x * trimDistance, p2.Y - u1y * trimDistance);

            // Arc end point (start of trimmed second segment)
            Point2D arcEnd = new Point2D(p2.X + u2x * trimDistance, p2.Y + u2y * trimDistance);

            // Determine if this is a left or right turn using cross product
            double cross = u1x * u2y - u1y * u2x;
            bool sweepClockwise = cross > 0;

            // Draw centerline arc (disabled for now)
            // var centerArc = CreateArcFromPoints(arcStart, arcEnd, bendRadius, sweepClockwise, Brushes.LightGray, 1);
            // if (centerArc != null)
            // {
            //     _canvas.Children.Add(centerArc);
            //     busbar.VisualShapes.Add(centerArc);
            // }

            // Draw edge arcs (inner and outer)
            DrawEdgeArcs(busbar, p1, p2, p3, bendRadius, halfWidth);
        }

        private void DrawEdgeArcs(Busbar busbar, Point2D p1, Point2D p2, Point2D p3, double centerRadius, double halfWidth)
        {
            // Draw two offset arcs that connect the edge lines
            // This matches the approach used in MainWindow.xaml.cs DrawEdgeArcs

            // Direction vectors
            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

            double dx2 = p3.X - p2.X;
            double dy2 = p3.Y - p2.Y;
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (len1 < 0.01 || len2 < 0.01) return;

            // Unit vectors
            double u1x = dx1 / len1;
            double u1y = dy1 / len1;
            double u2x = dx2 / len2;
            double u2y = dy2 / len2;

            // Calculate the angle between segments using dot product
            double dot = u1x * u2x + u1y * u2y;
            double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));

            if (Math.Abs(angleRad) < 0.001) return; // Nearly straight, no arc needed

            // Calculate the centerline arc's trim distance
            double centerTrimDistance = centerRadius * Math.Tan(angleRad / 2.0);

            // Calculate centerline arc endpoints
            Point2D centerArcStart = new Point2D(p2.X - u1x * centerTrimDistance, p2.Y - u1y * centerTrimDistance);
            Point2D centerArcEnd = new Point2D(p2.X + u2x * centerTrimDistance, p2.Y + u2y * centerTrimDistance);

            // Perpendicular vectors for offsetting (perpendicular to each segment direction)
            double perp1x = -u1y;
            double perp1y = u1x;
            double perp2x = -u2y;
            double perp2y = u2x;

            // Calculate edge arc start points (offset from centerline arc start)
            Point2D edge1ArcStart = new Point2D(centerArcStart.X + perp1x * halfWidth, centerArcStart.Y + perp1y * halfWidth);
            Point2D edge2ArcStart = new Point2D(centerArcStart.X - perp1x * halfWidth, centerArcStart.Y - perp1y * halfWidth);

            // Calculate edge arc end points (offset from centerline arc end)
            Point2D edge1ArcEnd = new Point2D(centerArcEnd.X + perp2x * halfWidth, centerArcEnd.Y + perp2y * halfWidth);
            Point2D edge2ArcEnd = new Point2D(centerArcEnd.X - perp2x * halfWidth, centerArcEnd.Y - perp2y * halfWidth);

            // Determine which edge is inner and which is outer based on turn direction
            double cross = u1x * u2y - u1y * u2x;
            bool sweepClockwise = cross > 0;

            // For the edge arcs, we need to calculate the actual radii
            // The offset arcs have different radii than the centerline
            double innerRadius = centerRadius - halfWidth;
            double outerRadius = centerRadius + halfWidth;

            // Determine which edge gets which radius based on turn direction
            // When turning clockwise (cross > 0), edge1 is inner, edge2 is outer
            // When turning counterclockwise (cross < 0), edge1 is outer, edge2 is inner
            double edge1Radius = sweepClockwise ? innerRadius : outerRadius;
            double edge2Radius = sweepClockwise ? outerRadius : innerRadius;

            // Draw first edge arc
            if (edge1Radius > 0)
            {
                var edge1Arc = CreateArcFromPoints(edge1ArcStart, edge1ArcEnd, edge1Radius, sweepClockwise, Brushes.Black, 0.5);
                if (edge1Arc != null)
                {
                    _canvas.Children.Add(edge1Arc);
                    busbar.VisualShapes.Add(edge1Arc);
                }
            }

            // Draw second edge arc
            var edge2Arc = CreateArcFromPoints(edge2ArcStart, edge2ArcEnd, edge2Radius, sweepClockwise, Brushes.Black, 0.5);
            if (edge2Arc != null)
            {
                _canvas.Children.Add(edge2Arc);
                busbar.VisualShapes.Add(edge2Arc);
            }
        }

        private System.Windows.Shapes.Path? CreateArcFromPoints(Point2D start, Point2D end, double radius, bool sweepClockwise, System.Windows.Media.Brush stroke, double strokeThickness)
        {
            var pathFigure = new PathFigure
            {
                StartPoint = new System.Windows.Point(start.X, start.Y)
            };

            var arcSegment = new ArcSegment
            {
                Point = new System.Windows.Point(end.X, end.Y),
                Size = new System.Windows.Size(radius, radius),
                SweepDirection = sweepClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                IsLargeArc = false
            };

            pathFigure.Segments.Add(arcSegment);

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            return new System.Windows.Shapes.Path
            {
                Data = pathGeometry,
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                IsHitTestVisible = false
            };
        }

        private System.Windows.Shapes.Path? CreateArc(Point2D center, double radius, double startAngle, double endAngle, bool isClockwise, System.Windows.Media.Brush stroke, double strokeThickness)
        {
            // Calculate start and end points
            Point2D startPoint = new Point2D(
                center.X + radius * Math.Cos(startAngle),
                center.Y + radius * Math.Sin(startAngle)
            );

            Point2D endPoint = new Point2D(
                center.X + radius * Math.Cos(endAngle),
                center.Y + radius * Math.Sin(endAngle)
            );

            // Calculate sweep angle
            double sweepAngle = endAngle - startAngle;
            if (isClockwise && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
            if (!isClockwise && sweepAngle < 0) sweepAngle += 2 * Math.PI;

            bool isLargeArc = Math.Abs(sweepAngle) > Math.PI;
            SweepDirection sweepDirection = isClockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure
            {
                StartPoint = new System.Windows.Point(startPoint.X, startPoint.Y),
                IsClosed = false
            };

            var arcSegment = new ArcSegment
            {
                Point = new System.Windows.Point(endPoint.X, endPoint.Y),
                Size = new System.Windows.Size(radius, radius),
                SweepDirection = sweepDirection,
                IsLargeArc = isLargeArc
            };

            pathFigure.Segments.Add(arcSegment);
            pathGeometry.Figures.Add(pathFigure);

            return new System.Windows.Shapes.Path
            {
                Data = pathGeometry,
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                IsHitTestVisible = false
            };
        }

        private Point2D TrimLineStart(Point2D p0, Point2D p1, Point2D p2, double radius)
        {
            double dx1 = p1.X - p0.X;
            double dy1 = p1.Y - p0.Y;
            double dx2 = p2.X - p1.X;
            double dy2 = p2.Y - p1.Y;

            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (len1 < 0.1 || len2 < 0.1) return p1;

            dx1 /= len1; dy1 /= len1;
            dx2 /= len2; dy2 /= len2;

            double dot = dx1 * dx2 + dy1 * dy2;
            dot = Math.Max(-1, Math.Min(1, dot));
            double angle = Math.Acos(dot);

            if (angle < 0.01) return p1;

            double trimDistance = radius * Math.Tan(angle / 2);
            trimDistance = Math.Min(trimDistance, len2 * 0.4);

            return new Point2D(
                p1.X + dx2 * trimDistance,
                p1.Y + dy2 * trimDistance
            );
        }

        private Point2D TrimLineEnd(Point2D p1, Point2D p2, Point2D p3, double radius)
        {
            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double dx2 = p3.X - p2.X;
            double dy2 = p3.Y - p2.Y;

            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

            if (len1 < 0.1 || len2 < 0.1) return p2;

            dx1 /= len1; dy1 /= len1;
            dx2 /= len2; dy2 /= len2;

            double dot = dx1 * dx2 + dy1 * dy2;
            dot = Math.Max(-1, Math.Min(1, dot));
            double angle = Math.Acos(dot);

            if (angle < 0.01) return p2;

            double trimDistance = radius * Math.Tan(angle / 2);
            trimDistance = Math.Min(trimDistance, len1 * 0.4);

            return new Point2D(
                p2.X - dx1 * trimDistance,
                p2.Y - dy1 * trimDistance
            );
        }

        #endregion
    }
}
