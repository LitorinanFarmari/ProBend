namespace BusbarCAD.Models
{
    public enum SegmentType
    {
        Start,
        Middle,
        End
    }

    public class Segment
    {
        public Point2D StartPoint { get; set; }
        public Point2D EndPoint { get; set; }
        public SegmentType Type { get; set; }
        public bool WasForcedToMinimum { get; set; } // Track if length was forced to minimum

        // Trim distances for bend arcs (how much is cut from each end for the arc)
        public double StartTrimDistance { get; set; } // Trim at start (0 for first segment)
        public double EndTrimDistance { get; set; }   // Trim at end (0 for last segment)

        // Bend angle before this segment (0 for first segment, bend angle for subsequent segments)
        public double BendAngle { get; set; }

        public Segment(Point2D start, Point2D end)
        {
            StartPoint = start;
            EndPoint = end;
            StartTrimDistance = 0;
            EndTrimDistance = 0;
            BendAngle = 0;
        }

        /// <summary>
        /// Segment length (centerline length from StartPoint to EndPoint)
        /// Setting this adjusts EndPoint while keeping the same direction
        /// </summary>
        public double Length
        {
            get => StartPoint.DistanceTo(EndPoint);
            set
            {
                // Adjust EndPoint to match new length, keeping same direction
                double angle = AngleRadians;
                EndPoint = new Point2D(
                    StartPoint.X + value * Math.Cos(angle),
                    StartPoint.Y + value * Math.Sin(angle)
                );
            }
        }

        /// <summary>
        /// Direction angle in radians
        /// </summary>
        public double AngleRadians
        {
            get
            {
                double dx = EndPoint.X - StartPoint.X;
                double dy = EndPoint.Y - StartPoint.Y;
                return Math.Atan2(dy, dx);
            }
        }

        /// <summary>
        /// Direction angle in degrees (derived from points)
        /// Setting this adjusts EndPoint to match new angle while keeping the same length
        /// </summary>
        public double Angle
        {
            get => AngleRadians * 180.0 / Math.PI;
            set
            {
                // Adjust EndPoint to match new angle, keeping same length
                double currentLength = Length;
                double newAngleRadians = value * Math.PI / 180.0; // Convert degrees to radians
                EndPoint = new Point2D(
                    StartPoint.X + currentLength * Math.Cos(newAngleRadians),
                    StartPoint.Y + currentLength * Math.Sin(newAngleRadians)
                );
            }
        }

        /// <summary>
        /// Straight section length (length minus trim distances for bends)
        /// This is the actual straight material length of this segment after accounting for bend trimming
        /// </summary>
        public double StraightSectionLength => Length - StartTrimDistance - EndTrimDistance;

        public override string ToString()
        {
            return $"Segment: {Length:F1}mm, Angle: {Angle:F1}°, Type: {Type}";
        }
    }
}
