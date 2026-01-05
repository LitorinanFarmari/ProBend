namespace BusbarCAD.Models
{
    public class MaterialSettings
    {
        public double BusbarWidth { get; set; }
        public double LayerSpacing { get; set; }
        public double BendToolRadius { get; set; }
        public double KFactor { get; set; }
        public double Thickness { get; set; }

        public MaterialSettings()
        {
            BusbarWidth = 80.0; // Default 80mm
            LayerSpacing = 20.0; // Default 20mm
            BendToolRadius = 20.0; // Default 20mm
            KFactor = 0.45; // Default for aluminum
            Thickness = 10.0; // Always 10mm (fixed)
        }

        public double GetTotalLayerHeight()
        {
            return BusbarWidth + LayerSpacing;
        }

        public override string ToString()
        {
            return $"Width: {BusbarWidth}mm, Spacing: {LayerSpacing}mm, K-Factor: {KFactor}";
        }
    }
}
