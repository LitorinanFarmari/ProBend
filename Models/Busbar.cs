using System.Collections.Generic;
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
        public List<Shape> VisualShapes { get; set; } = new List<Shape>();
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

        public override string ToString()
        {
            return $"{Name}: {Segments.Count} segments, {Bends.Count} bends, Valid: {IsValid}";
        }
    }
}
