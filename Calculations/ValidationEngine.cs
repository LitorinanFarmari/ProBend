using BusbarCAD.Models;

namespace BusbarCAD.Calculations
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }

        public ValidationResult()
        {
            IsValid = true;
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public void AddError(string error)
        {
            IsValid = false;
            Errors.Add(error);
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }

    public class ValidationEngine
    {
        // Validation rules from specification:
        // 1. Both end segments: Must be ≥ 50mm
        // 2. At least one end segment: Must be ≥ 70mm
        // 3. Middle segments (between bends): Must be ≥ 80mm

        private const double ABSOLUTE_MIN_END = 50.0;
        private const double PREFERRED_MIN_END = 70.0;
        private const double MIN_MIDDLE = 80.0;

        /// <summary>
        /// Validate a busbar against all dimensional rules
        /// </summary>
        public static ValidationResult ValidateBusbar(Busbar busbar)
        {
            var result = new ValidationResult();

            if (busbar.Segments.Count == 0)
            {
                result.AddError("Busbar has no segments");
                return result;
            }

            // Single segment (straight bar) has no restrictions
            if (busbar.Segments.Count == 1)
            {
                busbar.IsValid = true;
                return result;
            }

            // Validate end segments
            var endValidation = ValidateEndSegments(busbar);
            if (!endValidation.IsValid)
            {
                foreach (var error in endValidation.Errors)
                    result.AddError(error);
            }

            // Validate middle segments
            var middleValidation = ValidateMiddleSegments(busbar);
            if (!middleValidation.IsValid)
            {
                foreach (var error in middleValidation.Errors)
                    result.AddError(error);
            }

            busbar.IsValid = result.IsValid;
            return result;
        }

        /// <summary>
        /// Validate end segment rules
        /// </summary>
        public static ValidationResult ValidateEndSegments(Busbar busbar)
        {
            var result = new ValidationResult();

            if (busbar.Segments.Count < 2)
                return result;

            var firstSegment = busbar.Segments[0];
            var lastSegment = busbar.Segments[busbar.Segments.Count - 1];

            // Rule 1: Both ends must be ≥ 50mm (absolute minimum)
            if (firstSegment.InsideLength < ABSOLUTE_MIN_END)
            {
                if (firstSegment.WasForcedToMinimum)
                {
                    result.AddWarning($"First segment length forced to minimum (50mm)");
                }
                else
                {
                    result.AddError($"First segment ({firstSegment.InsideLength:F1}mm) is below absolute minimum ({ABSOLUTE_MIN_END}mm)");
                }
            }

            if (lastSegment.InsideLength < ABSOLUTE_MIN_END)
            {
                if (lastSegment.WasForcedToMinimum)
                {
                    result.AddWarning($"Last segment length forced to minimum (50mm)");
                }
                else
                {
                    result.AddError($"Last segment ({lastSegment.InsideLength:F1}mm) is below absolute minimum ({ABSOLUTE_MIN_END}mm)");
                }
            }

            // Rule 2: At least one end must be ≥ 70mm
            bool firstIsPreferred = firstSegment.InsideLength >= PREFERRED_MIN_END;
            bool lastIsPreferred = lastSegment.InsideLength >= PREFERRED_MIN_END;

            if (!firstIsPreferred && !lastIsPreferred)
            {
                if (firstSegment.WasForcedToMinimum || lastSegment.WasForcedToMinimum)
                {
                    result.AddWarning($"Neither end segment meets preferred minimum ({PREFERRED_MIN_END}mm). " +
                                    $"First: {firstSegment.InsideLength:F1}mm, Last: {lastSegment.InsideLength:F1}mm");
                }
                else
                {
                    result.AddError($"Neither end segment meets preferred minimum ({PREFERRED_MIN_END}mm). " +
                                  $"First: {firstSegment.InsideLength:F1}mm, Last: {lastSegment.InsideLength:F1}mm");
                }
            }

            return result;
        }

        /// <summary>
        /// Validate middle segment rules
        /// </summary>
        public static ValidationResult ValidateMiddleSegments(Busbar busbar)
        {
            var result = new ValidationResult();

            if (busbar.Segments.Count < 3)
                return result; // No middle segments

            // Check all middle segments (between first and last)
            for (int i = 1; i < busbar.Segments.Count - 1; i++)
            {
                var segment = busbar.Segments[i];
                if (segment.InsideLength < MIN_MIDDLE)
                {
                    if (segment.WasForcedToMinimum)
                    {
                        result.AddWarning($"Segment {i + 1} length forced to minimum (50mm)");
                    }
                    else
                    {
                        result.AddError($"Middle segment {i} ({segment.InsideLength:F1}mm) is below minimum ({MIN_MIDDLE}mm)");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a specific segment should be highlighted as invalid
        /// </summary>
        public static bool ShouldHighlightSegment(Busbar busbar, int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= busbar.Segments.Count)
                return false;

            var segment = busbar.Segments[segmentIndex];

            // Check if it's an end segment
            if (segmentIndex == 0 || segmentIndex == busbar.Segments.Count - 1)
            {
                // Highlight if below absolute minimum
                if (segment.InsideLength < ABSOLUTE_MIN_END)
                    return true;

                // Highlight if both ends are below preferred minimum
                if (busbar.Segments.Count >= 2)
                {
                    var firstLength = busbar.Segments[0].InsideLength;
                    var lastLength = busbar.Segments[busbar.Segments.Count - 1].InsideLength;

                    if (firstLength < PREFERRED_MIN_END && lastLength < PREFERRED_MIN_END)
                        return true;
                }
            }
            else
            {
                // Middle segment - highlight if below minimum
                if (segment.InsideLength < MIN_MIDDLE)
                    return true;
            }

            return false;
        }
    }
}
