namespace BusbarCAD.Models
{
    public class Bend
    {
        public Point2D Position { get; set; }
        public double Angle { get; set; } // Bend angle in degrees (+90 = clockwise, -90 = counter-clockwise)
        public double Radius { get; set; } // Tool radius
        public double BendAllowance { get; set; } // Calculated arc length

        public Bend(Point2D position, double angle, double radius)
        {
            Position = position;
            Angle = angle;
            Radius = radius;
            BendAllowance = 0; // Will be calculated later
        }

        public bool IsClockwise()
        {
            return Angle > 0;
        }

        public override string ToString()
        {
            return $"Bend: {Angle:F1}° at {Position}, R={Radius}mm";
        }
    }
}
