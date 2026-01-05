using BusbarCAD.Models;
using BusbarCAD.Calculations;

namespace BusbarCAD.Export
{
    public class BebFileGenerator
    {
        /// <summary>
        /// Generate .beb file content for a busbar
        /// </summary>
        public static string GenerateBebFile(Busbar busbar, MaterialSettings settings)
        {
            var sb = new StringBuilder();

            // Calculate flat length and backgauge positions
            double flatLength = BendCalculator.CalculateFlatLength(busbar, settings);
            var backgaugePositions = BendCalculator.CalculateBackgaugePositions(busbar, settings);

            // Header parameters
            sb.AppendLine($"<pl1> {flatLength:F6}");
            sb.AppendLine($"<pst> {settings.Thickness:F6}");
            sb.AppendLine("<ptm> MW 90 M");
            sb.AppendLine($"<pts> R {settings.BendToolRadius:F0} CU");
            sb.AppendLine($"<phh> {settings.BendToolRadius:F6}");
            sb.AppendLine("<pah> 0.000000");
            sb.AppendLine("<pyf> 0.000000");
            sb.AppendLine("<mat> 6 Aluminium"); // 6 = code for Aluminum
            sb.AppendLine("<mea> i"); // i = internal measurements (inside dimensions)
            sb.AppendLine("<pca> 0.000000");

            // Flange definitions (segments with angles)
            for (int i = 0; i < 40; i++)
            {
                if (i < busbar.Segments.Count)
                {
                    var segment = busbar.Segments[i];
                    double angle = (i < busbar.Bends.Count) ? busbar.Bends[i].Angle : 0.0;
                    sb.AppendLine($"<pfl> {i} {angle:F6} {segment.InsideLength:F6}");
                }
                else
                {
                    sb.AppendLine($"<pfl> {i} 0.000000 0.000000");
                }
            }

            // Line/bend definitions (backgauge positions)
            for (int i = 0; i < 40; i++)
            {
                if (i < backgaugePositions.Count)
                {
                    double angle = busbar.Bends[i].Angle;
                    double position = backgaugePositions[i];
                    int timestamp = 19940 + i; // Dummy timestamp
                    sb.AppendLine($"<pln> {i} {angle:F6} {position:F6} 0 0.000000 {timestamp}");
                }
                else
                {
                    sb.AppendLine($"<pln> {i} 0.000000 0.000000 0 0.000000 19939");
                }
            }

            // Hole/marking positions (not implemented in MVP)
            for (int i = 0; i < 40; i++)
            {
                sb.AppendLine($"<pml> {i} nan nan");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Write .beb file to disk
        /// </summary>
        public static bool WriteBebFile(string content, string filepath)
        {
            try
            {
                File.WriteAllText(filepath, content);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Generate filename for a busbar
        /// Pattern: [ProjectName]_[LayerName]_[BusbarName].bep
        /// </summary>
        public static string GenerateFileName(string projectName, string layerName, string busbarName)
        {
            // Sanitize names to remove invalid file characters
            string sanitized = $"{projectName}_{layerName}_{busbarName}";
            sanitized = string.Join("_", sanitized.Split(Path.GetInvalidFileNameChars()));
            return $"{sanitized}.bep";
        }

        /// <summary>
        /// Export all busbars in a project to .beb files
        /// </summary>
        public static int ExportProject(Project project, string exportDirectory)
        {
            int exportedCount = 0;

            // Create export directory if it doesn't exist
            if (!Directory.Exists(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
            }

            foreach (var layer in project.Layers)
            {
                foreach (var busbar in layer.Busbars)
                {
                    // Only export valid busbars
                    if (!busbar.IsValid)
                        continue;

                    string filename = GenerateFileName(project.Name, layer.Name, busbar.Name);
                    string filepath = Path.Combine(exportDirectory, filename);

                    string content = GenerateBebFile(busbar, project.MaterialSettings);

                    if (WriteBebFile(content, filepath))
                    {
                        exportedCount++;
                    }
                }
            }

            return exportedCount;
        }
    }
}
