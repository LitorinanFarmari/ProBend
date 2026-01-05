using System.Collections.Generic;

namespace BusbarCAD.Models
{
    public class Busbar
    {
        public string Name { get; set; }
        public List<Segment> Segments { get; set; }
        public List<Bend> Bends { get; set; }
        public bool IsValid { get; set; }
        public double FlatLength { get; set; }

        public Busbar(string name)
        {
            Name = name;
            Segments = new List<Segment>();
            Bends = new List<Bend>();
            IsValid = true;
            FlatLength = 0;
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

        public override string ToString()
        {
            return $"{Name}: {Segments.Count} segments, {Bends.Count} bends, Valid: {IsValid}";
        }
    }
}
