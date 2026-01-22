using BusbarCAD.Models;

namespace BusbarCAD.Calculations
{
    public class BendCalculator
    {
        /// <summary>
        /// Calculate the total flat length of a busbar accounting for bend allowances
        /// </summary>
        public static double CalculateFlatLength(Busbar busbar, MaterialSettings settings)
        {
            if (busbar.Segments.Count == 0)
                return 0;

            double totalLength = 0;
            double thickness = settings.Thickness;
            double toolRadius = settings.BendToolRadius;
            double kFactor = settings.KFactor;

            // Calculate straight sections and add bend allowances
            for (int i = 0; i < busbar.Segments.Count; i++)
            {
                // Add straight section length
                double straightLength = CalculateStraightLength(
                    busbar.Segments[i].Length,
                    thickness,
                    toolRadius,
                    i < busbar.Bends.Count, // Has bend after this segment
                    i > 0 // Has bend before this segment
                );

                totalLength += straightLength;

                // Add bend allowance if there's a bend after this segment
                if (i < busbar.Bends.Count)
                {
                    double bendAllowance = CalculateBendAllowance(
                        busbar.Bends[i].Angle,
                        toolRadius,
                        thickness,
                        kFactor
                    );
                    busbar.Bends[i].BendAllowance = bendAllowance;
                    totalLength += bendAllowance;
                }
            }

            busbar.FlatLength = totalLength;
            return totalLength;
        }

        /// <summary>
        /// Calculate the straight section length at centerline minus corner cuts
        /// </summary>
        private static double CalculateStraightLength(
            double insideDimension,
            double thickness,
            double toolRadius,
            bool hasBendAfter,
            bool hasBendBefore)
        {
            // Centerline radius = Tool radius + (Thickness / 2)
            double centerlineRadius = toolRadius + (thickness / 2.0);

            // Start with centerline length
            double centerlineLength = insideDimension + (thickness / 2.0);

            // Subtract corner cuts for bends
            if (hasBendAfter || hasBendBefore)
            {
                // For 90° bend: corner cut = centerlineRadius × tan(45°) = centerlineRadius
                // For other angles, we'll use the general formula
                double cornerCut = centerlineRadius; // Simplified for 90° bends in MVP

                if (hasBendAfter)
                    centerlineLength -= cornerCut;
                if (hasBendBefore)
                    centerlineLength -= cornerCut;
            }

            return centerlineLength;
        }

        /// <summary>
        /// Calculate bend allowance (arc length at neutral axis)
        /// Formula: Arc length = (Bend angle / 360°) × 2π × Neutral radius
        /// </summary>
        public static double CalculateBendAllowance(
            double bendAngleDegrees,
            double toolRadius,
            double thickness,
            double kFactor)
        {
            // Neutral axis radius = Tool radius + (K-factor × Thickness)
            double neutralRadius = toolRadius + (kFactor * thickness);

            // Convert angle to absolute value (handle both +90 and -90)
            double angleAbs = Math.Abs(bendAngleDegrees);

            // Arc length = (angle / 360) × 2π × radius
            double arcLength = (angleAbs / 360.0) * 2.0 * Math.PI * neutralRadius;

            return arcLength;
        }

        /// <summary>
        /// Calculate backgauge position for CNC machine
        /// Formula: First straight + (Arc / 2)
        /// </summary>
        public static List<double> CalculateBackgaugePositions(Busbar busbar, MaterialSettings settings)
        {
            var positions = new List<double>();

            if (busbar.Segments.Count == 0 || busbar.Bends.Count == 0)
                return positions;

            double thickness = settings.Thickness;
            double toolRadius = settings.BendToolRadius;
            double currentPosition = 0;

            for (int i = 0; i < busbar.Bends.Count; i++)
            {
                // Calculate straight section before this bend
                double straightLength = CalculateStraightLength(
                    busbar.Segments[i].Length,
                    thickness,
                    toolRadius,
                    true, // Has bend after
                    i > 0 // Has bend before
                );

                // Add half of the bend allowance
                double halfBend = busbar.Bends[i].BendAllowance / 2.0;

                currentPosition += straightLength + halfBend;
                positions.Add(currentPosition);

                // Add the other half of the bend to continue
                currentPosition += halfBend;
            }

            return positions;
        }

        /// <summary>
        /// Reverse-calculate K-factor from measured flat length (for calibration wizard)
        /// </summary>
        public static double CalibrateKFactor(
            double measuredFlatLength,
            double insideDimension1,
            double insideDimension2,
            double bendAngle,
            double toolRadius,
            double thickness)
        {
            // Calculate total straight sections (without bend allowance)
            double centerlineRadius = toolRadius + (thickness / 2.0);
            double cornerCut = centerlineRadius; // For 90° bend

            double straight1 = insideDimension1 + (thickness / 2.0) - cornerCut;
            double straight2 = insideDimension2 + (thickness / 2.0) - cornerCut;
            double totalStraights = straight1 + straight2;

            // Calculate required arc length
            double requiredArc = measuredFlatLength - totalStraights;

            // Back-calculate neutral radius
            double angleAbs = Math.Abs(bendAngle);
            double neutralRadius = requiredArc / ((angleAbs / 360.0) * 2.0 * Math.PI);

            // Calculate K-factor
            double kFactor = (neutralRadius - toolRadius) / thickness;

            return kFactor;
        }
    }
}
