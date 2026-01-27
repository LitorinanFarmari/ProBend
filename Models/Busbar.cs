using System.Collections.Generic;
using System.Windows;
using System.Windows.Shapes;

namespace BusbarCAD.Models
{
    public class Busbar
    {
        public string Name { get; set; }
        public List<Segment> Segments { get; set; }
        public List<Bend> Bends { get; set; }
        public bool IsValid { get; set; }
        public double FlatLength { get; set; }

        // Visual elements (for rendering and cleanup)
        public List<UIElement> VisualShapes { get; set; } = new List<UIElement>();
        public Line? StartMarker { get; set; } = null;
        public Line? EndMarker { get; set; } = null;

        public Busbar(string name)
        {
            Name = name;
            Segments = new List<Segment>();
            Bends = new List<Bend>();
            IsValid = true;
            FlatLength = 0;
        }

        /// <summary>
        /// Clears all visual shapes from this busbar (call before redrawing)
        /// </summary>
        public void ClearVisuals()
        {
            VisualShapes.Clear();
            StartMarker = null;
            EndMarker = null;
        }

        public void AddSegment(Segment segment)
        {
            Segments.Add(segment);
            UpdateSegmentTypes();
        }

        public void AddBend(Bend bend)
        {
            Bends.Add(bend);
        }

        private void UpdateSegmentTypes()
        {
            if (Segments.Count == 0) return;

            if (Segments.Count == 1)
            {
                Segments[0].Type = SegmentType.Start;
            }
            else
            {
                Segments[0].Type = SegmentType.Start;
                Segments[Segments.Count - 1].Type = SegmentType.End;

                for (int i = 1; i < Segments.Count - 1; i++)
                {
                    Segments[i].Type = SegmentType.Middle;
                }
            }
        }

        /// <summary>
        /// Moves a busbar point to a new position and updates all affected segments.
        /// Points are indexed 0 to N (where N = Segments.Count).
        /// Moving a point affects both the segment ending at that point and the segment starting from it.
        /// </summary>
        /// <param name="pointIndex">The index of the point to move (0 = first point, N = last point)</param>
        /// <param name="newPosition">The new position for the point</param>
        public void MoveBusbarPoint(int pointIndex, Point2D newPosition)
        {
            if (Segments.Count == 0) return;
            if (pointIndex < 0 || pointIndex > Segments.Count) return;

            Point2D oldPosition;

            // Get the old position of the point
            if (pointIndex == 0)
            {
                oldPosition = Segments[0].StartPoint;
            }
            else
            {
                oldPosition = Segments[pointIndex - 1].EndPoint;
            }

            // Calculate the offset
            Point2D offset = new Point2D(
                newPosition.X - oldPosition.X,
                newPosition.Y - oldPosition.Y
            );

            // Update the segment(s) that use this point
            if (pointIndex > 0)
            {
                // Update the EndPoint of the segment before this point
                Segments[pointIndex - 1].EndPoint = newPosition;
            }

            if (pointIndex < Segments.Count)
            {
                // Update the StartPoint of the segment at this point
                Segments[pointIndex].StartPoint = newPosition;

                // Also shift this segment's EndPoint by the offset to maintain its shape
                Segments[pointIndex].EndPoint = new Point2D(
                    Segments[pointIndex].EndPoint.X + offset.X,
                    Segments[pointIndex].EndPoint.Y + offset.Y
                );
            }

            // Propagate the movement to all subsequent segments (shift them by the offset)
            for (int i = pointIndex + 1; i < Segments.Count; i++)
            {
                Segments[i].StartPoint = new Point2D(
                    Segments[i].StartPoint.X + offset.X,
                    Segments[i].StartPoint.Y + offset.Y
                );
                Segments[i].EndPoint = new Point2D(
                    Segments[i].EndPoint.X + offset.X,
                    Segments[i].EndPoint.Y + offset.Y
                );
            }

            // If we moved the last point, update the last segment's endpoint
            if (pointIndex == Segments.Count && Segments.Count > 0)
            {
                Segments[Segments.Count - 1].EndPoint = newPosition;
            }
        }

        /// <summary>
        /// Calculates the total cut length of the busbar.
        /// Includes straight sections and bend allowances calculated with K-factor.
        /// </summary>
        /// <param name="toolRadius">Bend tool radius in mm</param>
        /// <param name="thickness">Busbar thickness in mm</param>
        /// <param name="kFactor">K-factor for bend allowance calculation (typically 0.9)</param>
        public double CalculateCutLength(double toolRadius, double thickness, double kFactor)
        {
            // First, calculate and set trim distances for all segments
            // Centerline radius for trim calculation
            double centerlineRadius = toolRadius + (thickness / 2.0);

            for (int i = 0; i < Segments.Count; i++)
            {
                // Reset trim distances
                Segments[i].StartTrimDistance = 0;
                Segments[i].EndTrimDistance = 0;

                // Calculate trim at start (if there's a bend before this segment)
                if (i > 0 && i - 1 < Bends.Count)
                {
                    double bendAngle = Math.Abs(Bends[i - 1].Angle);
                    Segments[i].StartTrimDistance = CalculateTrimDistance(centerlineRadius, bendAngle);
                }

                // Calculate trim at end (if there's a bend after this segment)
                if (i < Bends.Count)
                {
                    double bendAngle = Math.Abs(Bends[i].Angle);
                    Segments[i].EndTrimDistance = CalculateTrimDistance(centerlineRadius, bendAngle);
                }
            }

            // Sum all straight section lengths (segment length minus trim distances)
            double straightLength = 0;
            foreach (var segment in Segments)
            {
                straightLength += segment.StraightSectionLength;
            }

            // Calculate bend allowance using machine-specific formula: toolRadius + ((thickness/2) * kFactor)
            double bendAllowanceRadius = toolRadius + ((thickness / 2.0) * kFactor);

            // Sum all bend allowances (arc length at bend allowance radius)
            double bendAllowanceTotal = 0;
            foreach (var bend in Bends)
            {
                // Convert angle to radians (angle is in degrees)
                double angleRadians = Math.Abs(bend.Angle) * Math.PI / 180.0;

                // Calculate arc length: angle × radius
                double arcLength = angleRadians * bendAllowanceRadius;

                // Store the calculated bend allowance for future reference
                bend.BendAllowance = arcLength;

                bendAllowanceTotal += arcLength;
            }

            return straightLength + bendAllowanceTotal;
        }

        /// <summary>
        /// Calculate the trim distance for a bend based on centerline radius and bend angle
        /// For 90° bends: trim = centerlineRadius
        /// For other angles: trim = centerlineRadius × tan(angle/2)
        /// </summary>
        private static double CalculateTrimDistance(double centerlineRadius, double bendAngleDegrees)
        {
            // Convert to radians
            double angleRadians = Math.Abs(bendAngleDegrees) * Math.PI / 180.0;

            // Calculate trim: radius × tan(angle/2)
            double trimDistance = centerlineRadius * Math.Tan(angleRadians / 2.0);

            return trimDistance;
        }

        public override string ToString()
        {
            return $"{Name}: {Segments.Count} segments, {Bends.Count} bends, Valid: {IsValid}";
        }
    }
}
