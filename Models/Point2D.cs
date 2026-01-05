namespace BusbarCAD.Models
{
    public struct Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double DistanceTo(Point2D other)
        {
            double dx = other.X - X;
            double dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }
}
