using System.Collections.Generic;

namespace BusbarCAD.Models
{
    public class Layer
    {
        public string Name { get; set; }
        public double ZPosition { get; set; }
        public string Color { get; set; }
        public bool Visible { get; set; }
        public List<Busbar> Busbars { get; set; }

        public Layer(string name, double zPosition)
        {
            Name = name;
            ZPosition = zPosition;
            Color = "#333333"; // Default dark gray
            Visible = true;
            Busbars = new List<Busbar>();
        }

        public void AddBusbar(Busbar busbar)
        {
            Busbars.Add(busbar);
        }

        public void RemoveBusbar(Busbar busbar)
        {
            Busbars.Remove(busbar);
        }

        public int GetBusbarCount()
        {
            return Busbars.Count;
        }

        public override string ToString()
        {
            return $"{Name} (Z={ZPosition}mm): {Busbars.Count} busbars";
        }
    }
}
