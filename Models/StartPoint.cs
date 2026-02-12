namespace BusbarCAD.Models
{
    public class StartPoint
    {
        public Point2D Position { get; set; }

        public StartPoint(Point2D position)
        {
            Position = position;
        }
    }
}
