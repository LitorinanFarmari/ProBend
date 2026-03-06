namespace BusbarCAD.Models
{
    public class StartPoint
    {
        public int Id { get; set; }
        public Point2D Position { get; set; }

        public StartPoint() { } // Parameterless constructor for JSON deserialization

        public StartPoint(int id, Point2D position)
        {
            Id = id;
            Position = position;
        }
    }

    public class StartPointConnection
    {
        public int FromId { get; set; }
        public int ToId { get; set; }

        public StartPointConnection() { } // JSON deserialization

        public StartPointConnection(int fromId, int toId)
        {
            FromId = fromId;
            ToId = toId;
        }
    }
}
