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
        public double InsideLength { get; set; }
        public double Angle { get; set; } // Direction angle in degrees
        public SegmentType Type { get; set; }

        public Segment(Point2D start, Point2D end, double insideLength)
        {
            StartPoint = start;
            EndPoint = end;
            InsideLength = insideLength;
            CalculateAngle();
        }

        private void CalculateAngle()
        {
            double dx = EndPoint.X - StartPoint.X;
            double dy = EndPoint.Y - StartPoint.Y;
            Angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        }

        public double GetLength()
        {
            return StartPoint.DistanceTo(EndPoint);
        }

        public override string ToString()
        {
            return $"Segment: {InsideLength:F1}mm, Angle: {Angle:F1}°, Type: {Type}";
        }
    }
}
