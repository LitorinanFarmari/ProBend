using System.Collections.Generic;
using System.Windows;
using Newtonsoft.Json;

namespace BusbarCAD.Models
{
    public enum MeasurementType
    {
        Direct,
        Horizontal,
        Vertical
    }

    public class Measurement
    {
        // Point references (busbar name + point index for live updates)
        public string BusbarName1 { get; set; }
        public int PointIndex1 { get; set; }
        public string BusbarName2 { get; set; }
        public int PointIndex2 { get; set; }

        public MeasurementType Type { get; set; }
        public double Offset { get; set; } // Perpendicular distance of dimension line from measured line

        [JsonIgnore] public List<UIElement> VisualShapes { get; set; } = new List<UIElement>();
        [JsonIgnore] public bool IsSelected { get; set; }

        public Measurement() { } // Parameterless constructor for JSON deserialization

        public Measurement(string busbarName1, int pointIndex1, string busbarName2, int pointIndex2,
            MeasurementType type, double offset)
        {
            BusbarName1 = busbarName1;
            PointIndex1 = pointIndex1;
            BusbarName2 = busbarName2;
            PointIndex2 = pointIndex2;
            Type = type;
            Offset = offset;
        }
    }
}
