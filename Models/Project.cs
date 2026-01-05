using System;
using System.Collections.Generic;

namespace BusbarCAD.Models
{
    public class Project
    {
        public string Name { get; set; }
        public string CustomerInfo { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public MaterialSettings MaterialSettings { get; set; }
        public List<Layer> Layers { get; set; }
        public string ExportPath { get; set; }

        public Project(string name)
        {
            Name = name;
            CustomerInfo = string.Empty;
            DateCreated = DateTime.Now;
            DateModified = DateTime.Now;
            MaterialSettings = new MaterialSettings();
            Layers = new List<Layer>();
            ExportPath = string.Empty;

            // Create default layer for MVP
            AddDefaultLayer();
        }

        private void AddDefaultLayer()
        {
            var layer = new Layer("Layer 1", 0);
            Layers.Add(layer);
        }

        public Layer GetActiveLayer()
        {
            return Layers.Count > 0 ? Layers[0] : null;
        }

        public double GetAssemblyTotalHeight()
        {
            return Layers.Count * MaterialSettings.GetTotalLayerHeight();
        }

        public void UpdateModifiedDate()
        {
            DateModified = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{Name}: {Layers.Count} layers, Created: {DateCreated:d}";
        }
    }
}
