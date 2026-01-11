using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BusbarCAD.Models;
using BusbarCAD.Calculations;
using BusbarCAD.Export;
using Microsoft.Win32;

namespace BusbarCAD;

/// <summary>
/// Segment information for display in the DataGrid
/// </summary>
public class SegmentInfo
{
    public string Angle { get; set; } = "";
    public string Length { get; set; } = "";
}

/// <summary>
/// Saved busbar with name and segments
/// </summary>
public class SavedBusbar
{
    public string Name { get; set; } = "";
    public List<Point2D> Points { get; set; } = new List<Point2D>();
    public List<SegmentInfo> Segments { get; set; } = new List<SegmentInfo>();
    public List<Shape> Shapes { get; set; } = new List<Shape>(); // Visual shapes on canvas
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Project _currentProject;
    private bool _isDrawing = false;
    private List<Point2D> _currentPoints = new List<Point2D>();
    private Polygon? _previewPolygon = null;
    private List<SegmentInfo> _currentSegments = new List<SegmentInfo>();
    private List<SavedBusbar> _savedBusbars = new List<SavedBusbar>();
    private List<Shape> _currentShapes = new List<Shape>(); // Track shapes being drawn
    private int _currentBusbarIndex = -1; // Index of the busbar being edited (-1 if new)
    private char _nextBusbarLetter = 'a'; // Next letter to use for busbar naming
    private TransformGroup _canvasTransform;
    private ScaleTransform _scaleTransform;
    private TranslateTransform _translateTransform;
    private bool _isPanning = false;
    private System.Windows.Point _lastPanPoint;
    private bool _handToolActive = false;
    private bool _waitingForLengthInput = false;
    private double _pendingAngle = 0;
    private System.Windows.Threading.DispatcherTimer? _mouseStopTimer;
    private bool _isEditingLength = false;
    private System.Windows.Controls.TextBox? _currentEditingTextBox = null;
    private Ellipse? _previewPoint = null; // Preview point that follows cursor
    private Line? _previewLine = null; // Preview line from last point to cursor
    private System.Windows.Shapes.Path? _previewArc = null; // Preview arc at the previous bend
    private List<Shape> _previewMarkers = new List<Shape>(); // Preview perpendicular markers and edge arcs
    private List<bool> _segmentsForcedToMinimum = new List<bool>(); // Track which segments were forced to minimum length

    public MainWindow()
    {
        InitializeComponent();
        InitializeCanvasTransform();
        InitializeProject();
        this.PreviewKeyDown += Window_KeyDown;
    }

    private void InitializeCanvasTransform()
    {
        // Set up transform group for canvas scaling and panning
        _scaleTransform = new ScaleTransform(1, 1);
        _translateTransform = new TranslateTransform(0, 0);
        _canvasTransform = new TransformGroup();
        _canvasTransform.Children.Add(_scaleTransform);
        _canvasTransform.Children.Add(_translateTransform);
        drawingCanvas.RenderTransform = _canvasTransform;

        // Add mouse wheel zoom handler
        drawingCanvas.MouseWheel += Canvas_MouseWheel;
        drawingCanvas.MouseDown += Canvas_MouseDown;
        drawingCanvas.MouseUp += Canvas_MouseUp;

        // Set initial scale to show ~200mm visible
        SetInitialScale();
    }

    private void DrawingCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Center the instruction text when canvas size changes
        if (txtInstructions != null && drawingCanvas.ActualWidth > 0 && drawingCanvas.ActualHeight > 0)
        {
            Canvas.SetLeft(txtInstructions, (drawingCanvas.ActualWidth - txtInstructions.ActualWidth) / 2);
            Canvas.SetTop(txtInstructions, (drawingCanvas.ActualHeight - txtInstructions.ActualHeight) / 2);
        }
    }

    private void SetInitialScale()
    {
        // Calculate scale so that 200mm fits comfortably in the canvas width
        double targetVisibleMm = 200.0;
        double canvasWidth = drawingCanvas.ActualWidth > 0 ? drawingCanvas.ActualWidth : 800; // Fallback width
        double initialScale = (canvasWidth * 0.8) / targetVisibleMm; // 80% of canvas width

        _scaleTransform.ScaleX = initialScale;
        _scaleTransform.ScaleY = initialScale;

        // Start with no translation - user can pan as needed
        _translateTransform.X = 0;
        _translateTransform.Y = 0;
    }

    private void InitializeProject()
    {
        _currentProject = new Project("Untitled Project");
        UpdateUI();
        UpdateStatusBar("New project created. Ready to draw.");
    }

    private void UpdateUI()
    {
        txtCurrentLayer.Text = _currentProject.GetActiveLayer()?.Name ?? "No Layer";
        RefreshBusbarList();
        UpdateValidationStatus();
    }

    private void RefreshBusbarList()
    {
        lstBusbars.Items.Clear();
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer != null)
        {
            foreach (var busbar in activeLayer.Busbars)
            {
                lstBusbars.Items.Add($"{busbar.Name} - {busbar.Segments.Count} segments");
            }
        }
    }

    private void UpdateValidationStatus()
    {
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0)
        {
            txtValidationStatus.Text = "No busbars";
            txtValidationStatus.Foreground = Brushes.Gray;
            return;
        }

        bool allValid = true;
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var busbar in activeLayer.Busbars)
        {
            var result = ValidationEngine.ValidateBusbar(busbar);
            if (!result.IsValid)
            {
                allValid = false;
                errors.AddRange(result.Errors);
            }
            warnings.AddRange(result.Warnings);
        }

        if (allValid && warnings.Count == 0)
        {
            txtValidationStatus.Text = $"All busbars valid ({activeLayer.Busbars.Count} total)";
            txtValidationStatus.Foreground = Brushes.Green;
        }
        else if (!allValid)
        {
            txtValidationStatus.Text = $"Validation errors:\n{string.Join("\n", errors.Take(3))}";
            txtValidationStatus.Foreground = Brushes.Red;
        }
        else
        {
            // Only warnings, no errors
            txtValidationStatus.Text = $"Validation warnings:\n{string.Join("\n", warnings.Take(3))}";
            txtValidationStatus.Foreground = Brushes.Orange;
        }
    }

    private void UpdateStatusBar(string message)
    {
        txtStatusBar.Text = message;
    }

    // Menu Handlers
    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Create new project? Unsaved changes will be lost.", "New Project",
            MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            drawingCanvas.Children.Clear();
            InitializeProject();
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Project load functionality coming soon!", "Open Project");
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Project save functionality coming soon!", "Save Project");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0)
        {
            MessageBox.Show("No busbars to export", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Check for validation errors
        int invalidCount = activeLayer.Busbars.Count(b => !b.IsValid);
        if (invalidCount > 0)
        {
            var result = MessageBox.Show(
                $"{invalidCount} busbar(s) have validation errors and will be skipped.\n\nContinue with export?",
                "Validation Warnings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
                return;
        }

        // Select export directory
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Select export directory for .bep files";
        dialog.ShowNewFolderButton = true;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string exportPath = dialog.SelectedPath;

            try
            {
                int count = BebFileGenerator.ExportProject(_currentProject, exportPath);

                MessageBox.Show(
                    $"Successfully exported {count} busbar(s) to:\n{exportPath}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                UpdateStatusBar($"Exported {count} .bep files");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during export:\n{ex.Message}",
                    "Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Undo functionality coming soon!", "Undo");
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Redo functionality coming soon!", "Redo");
    }

    private void DeleteBusbar_Click(object sender, RoutedEventArgs e)
    {
        if (lstBusbars.SelectedIndex >= 0)
        {
            var activeLayer = _currentProject.GetActiveLayer();
            if (activeLayer != null && activeLayer.Busbars.Count > lstBusbars.SelectedIndex)
            {
                activeLayer.Busbars.RemoveAt(lstBusbars.SelectedIndex);
                RedrawCanvas();
                UpdateUI();
                UpdateStatusBar("Busbar deleted");
            }
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Busbar CAD - MVP v1.0\nSpecialized CAD for bent busbars\n\nBuild with Claude Code",
            "About Busbar CAD");
    }

    // Drawing Mode
    private void DrawBusbar_Click(object sender, RoutedEventArgs e)
    {
        StartDrawing();
    }

    // Zoom Controls
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomAtCenter(1.1);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomAtCenter(1.0 / 1.1);
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        AutoScaleToFitAll();
    }

    private void Hand_Click(object sender, RoutedEventArgs e)
    {
        _handToolActive = !_handToolActive;

        // Update button appearance
        if (_handToolActive)
        {
            btnHand.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            drawingCanvas.Cursor = System.Windows.Input.Cursors.Hand;
            UpdateStatusBar("Hand tool active - Click and drag to pan");
        }
        else
        {
            btnHand.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            drawingCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
            UpdateStatusBar("Hand tool deactivated");
        }
    }

    private void ZoomAtCenter(double zoomFactor)
    {
        // Zoom centered on canvas
        double centerX = drawingCanvas.ActualWidth / 2;
        double centerY = drawingCanvas.ActualHeight / 2;

        var centerPoint = new System.Windows.Point(centerX, centerY);

        // Transform center position to canvas space
        var inverseTransform = _canvasTransform.Inverse;
        if (inverseTransform != null)
        {
            var canvasPoint = inverseTransform.Transform(centerPoint);

            _scaleTransform.ScaleX *= zoomFactor;
            _scaleTransform.ScaleY *= zoomFactor;

            // Keep center point fixed
            _translateTransform.X = centerX - canvasPoint.X * _scaleTransform.ScaleX;
            _translateTransform.Y = centerY - canvasPoint.Y * _scaleTransform.ScaleY;
        }
    }

    private void AutoScaleToFitAll()
    {
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0)
        {
            SetInitialScale();
            return;
        }

        // Calculate bounds of all busbars
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        foreach (var busbar in activeLayer.Busbars)
        {
            foreach (var segment in busbar.Segments)
            {
                minX = Math.Min(minX, Math.Min(segment.StartPoint.X, segment.EndPoint.X));
                maxX = Math.Max(maxX, Math.Max(segment.StartPoint.X, segment.EndPoint.X));
                minY = Math.Min(minY, Math.Min(segment.StartPoint.Y, segment.EndPoint.Y));
                maxY = Math.Max(maxY, Math.Max(segment.StartPoint.Y, segment.EndPoint.Y));
            }
        }

        // Add margin
        const double margin = 50;
        double busbarLeft = minX - margin;
        double busbarTop = minY - margin;
        double busbarRight = maxX + margin;
        double busbarBottom = maxY + margin;

        double totalWidth = busbarRight - busbarLeft;
        double totalHeight = busbarBottom - busbarTop;

        // Get canvas actual size
        double canvasWidth = drawingCanvas.ActualWidth > 0 ? drawingCanvas.ActualWidth : 800;
        double canvasHeight = drawingCanvas.ActualHeight > 0 ? drawingCanvas.ActualHeight : 600;

        // Calculate scale to fit
        double scaleX = (canvasWidth * 0.9) / totalWidth;
        double scaleY = (canvasHeight * 0.9) / totalHeight;
        double scale = Math.Min(scaleX, scaleY);

        // Apply scale
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;

        // Center the busbars
        _translateTransform.X = (canvasWidth - totalWidth * scale) / 2 - (busbarLeft * scale);
        _translateTransform.Y = (canvasHeight - totalHeight * scale) / 2 - (busbarTop * scale);
    }

    private void StartDrawing()
    {
        _isDrawing = true;
        _currentPoints.Clear();
        _currentSegments.Clear();
        _currentShapes.Clear();
        _segmentsForcedToMinimum.Clear();
        _currentBusbarIndex = -1;
        _waitingForLengthInput = false;  // Reset input state
        _isEditingLength = false;
        _mouseStopTimer?.Stop();  // Stop any running timer
        UpdateSegmentList();
        txtBusbarName.Text = _nextBusbarLetter.ToString();

        // Clear the busbar selection when starting a new drawing
        lstBusbars.SelectedIndex = -1;

        txtInstructions.Visibility = Visibility.Collapsed;

        // Disable Tab navigation during drawing to prevent focus changes
        KeyboardNavigation.SetTabNavigation(drawingCanvas, KeyboardNavigationMode.None);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);

        // Set focus to canvas to ensure mouse events work properly
        drawingCanvas.Focus();

        UpdateStatusBar("Drawing mode: Click to add points. Right-click or ESC to finish.");
    }

    private void UpdateSegmentList()
    {
        dgSegments.ItemsSource = null;
        dgSegments.ItemsSource = _currentSegments;
    }

    private void AddSegment(double angle, double length)
    {
        var segment = new SegmentInfo
        {
            Angle = angle.ToString("F1"),
            Length = length.ToString("F1")
        };
        _currentSegments.Add(segment);
        UpdateSegmentList();
    }

    private void SaveOrUpdateCurrentBusbar()
    {
        if (_currentPoints.Count < 2) return;

        // Get the busbar name from the textbox
        string busbarName = string.IsNullOrWhiteSpace(txtBusbarName.Text) ? "a" : txtBusbarName.Text;

        if (_currentBusbarIndex == -1)
        {
            // Create new SavedBusbar
            var savedBusbar = new SavedBusbar
            {
                Name = busbarName,
                Points = new List<Point2D>(_currentPoints),
                Segments = new List<SegmentInfo>(_currentSegments),
                Shapes = new List<Shape>(_currentShapes)
            };

            _savedBusbars.Add(savedBusbar);
            _currentBusbarIndex = _savedBusbars.Count - 1;

            // Add to the left panel ListBox
            lstBusbars.Items.Add(busbarName);
        }
        else
        {
            // Update existing SavedBusbar
            var savedBusbar = _savedBusbars[_currentBusbarIndex];
            savedBusbar.Name = busbarName;
            savedBusbar.Points = new List<Point2D>(_currentPoints);
            savedBusbar.Segments = new List<SegmentInfo>(_currentSegments);
            savedBusbar.Shapes = new List<Shape>(_currentShapes);

            // Update the ListBox item
            lstBusbars.Items[_currentBusbarIndex] = busbarName;
        }
    }

    private void FinishDrawing()
    {
        if (_currentPoints.Count < 2)
        {
            UpdateStatusBar("Need at least 2 points to create a busbar");
            CancelDrawing();
            return;
        }

        // Redraw centerlines without any preview trimming to ensure the last segment is correct
        RedrawCenterlines();

        // Get the busbar name from the textbox
        string busbarName = string.IsNullOrWhiteSpace(txtBusbarName.Text) ? "a" : txtBusbarName.Text;

        // Make sure the busbar is saved (it should already be from SaveOrUpdateCurrentBusbar)
        // But we need to create the project model busbar
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer != null)
        {
            int busbarNumber = activeLayer.Busbars.Count + 1;
            var busbar = new Busbar(busbarName);

            // Create segments and bends
            for (int i = 0; i < _currentPoints.Count - 1; i++)
            {
                var start = _currentPoints[i];
                var end = _currentPoints[i + 1];
                double length = start.DistanceTo(end);

                var segment = new Segment(start, end, length);

                // Set the WasForcedToMinimum flag if we have tracking data for this segment
                if (i < _segmentsForcedToMinimum.Count)
                {
                    segment.WasForcedToMinimum = _segmentsForcedToMinimum[i];
                }

                busbar.AddSegment(segment);

                // Add bend if not the last segment
                if (i < _currentPoints.Count - 2)
                {
                    // Calculate bend angle
                    double angle = CalculateBendAngle(i);
                    var bend = new Bend(end, angle, _currentProject.MaterialSettings.BendToolRadius);
                    busbar.AddBend(bend);
                }
            }

            // Calculate flat length
            BendCalculator.CalculateFlatLength(busbar, _currentProject.MaterialSettings);

            // Validate
            ValidationEngine.ValidateBusbar(busbar);

            activeLayer.AddBusbar(busbar);
        }

        // Clean up drawing state
        _isDrawing = false;
        _currentPoints.Clear();
        _currentSegments.Clear();
        _currentShapes.Clear();
        _segmentsForcedToMinimum.Clear();

        // Re-enable Tab navigation after drawing ends
        KeyboardNavigation.SetTabNavigation(drawingCanvas, KeyboardNavigationMode.Continue);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);

        // Keep the busbar selected in the list
        int finishedBusbarIndex = _currentBusbarIndex;
        _currentBusbarIndex = -1;

        // Remove preview shapes if they exist
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }
        if (_previewPoint != null)
        {
            drawingCanvas.Children.Remove(_previewPoint);
            _previewPoint = null;
        }
        if (_previewLine != null)
        {
            drawingCanvas.Children.Remove(_previewLine);
            _previewLine = null;
        }
        if (_previewArc != null)
        {
            drawingCanvas.Children.Remove(_previewArc);
            _previewArc = null;
        }
        foreach (var marker in _previewMarkers)
        {
            drawingCanvas.Children.Remove(marker);
        }
        _previewMarkers.Clear();

        // Select the finished busbar in the ListBox
        if (finishedBusbarIndex >= 0 && finishedBusbarIndex < lstBusbars.Items.Count)
        {
            lstBusbars.SelectedIndex = finishedBusbarIndex;
        }

        // Increment to next letter for next busbar (a -> b -> c ... z -> aa -> ab ...)
        if (_nextBusbarLetter == 'z')
        {
            _nextBusbarLetter = 'a'; // Could be extended to 'aa', 'ab', etc. if needed
        }
        else
        {
            _nextBusbarLetter++;
        }

        UpdateUI();
        UpdateStatusBar($"Busbar '{busbarName}' finished");
    }

    private double CalculateBendAngle(int segmentIndex)
    {
        if (segmentIndex >= _currentPoints.Count - 2) return 0;

        var p1 = _currentPoints[segmentIndex];
        var p2 = _currentPoints[segmentIndex + 1];
        var p3 = _currentPoints[segmentIndex + 2];

        // Calculate angles of the two segments
        double angle1 = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
        double angle2 = Math.Atan2(p3.Y - p2.Y, p3.X - p2.X);

        // Difference in angles
        double bendAngle = (angle2 - angle1) * 180.0 / Math.PI;

        // Normalize to -180 to 180
        while (bendAngle > 180) bendAngle -= 360;
        while (bendAngle < -180) bendAngle += 360;

        // Snap to common angles
        if (Math.Abs(bendAngle - 90) < 5) return 90;
        if (Math.Abs(bendAngle + 90) < 5) return -90;
        if (Math.Abs(bendAngle) < 5) return 0;
        if (Math.Abs(Math.Abs(bendAngle) - 180) < 5) return 180;

        return bendAngle;
    }

    private Point2D CalculateBendPivotPoint(Point2D p1, Point2D p2, Point2D p3)
    {
        // Calculate the bend pivot point based on inner radius
        // p1 = start of previous segment
        // p2 = end of previous segment (bend point)
        // p3 = proposed end of new segment

        const double busbarWidth = 10.0;
        double halfWidth = busbarWidth / 2.0;
        double bendRadius = _currentProject.MaterialSettings.BendToolRadius;

        // Calculate direction vectors
        double dx1 = p2.X - p1.X;
        double dy1 = p2.Y - p1.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p3.X - p2.X;
        double dy2 = p3.Y - p2.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.001 || len2 < 0.001) return p3; // Return original if too short

        // Normalized direction vectors
        double dir1X = dx1 / len1;
        double dir1Y = dy1 / len1;
        double dir2X = dx2 / len2;
        double dir2Y = dy2 / len2;

        // Perpendicular vectors (to the right)
        double perp1X = -dir1Y;
        double perp1Y = dir1X;

        // Determine bend direction (cross product)
        double cross = dir1X * dir2Y - dir1Y * dir2X;

        if (Math.Abs(cross) < 0.001) return p3; // Nearly parallel, no adjustment needed

        // Calculate the bend angle
        double angle1 = Math.Atan2(dir1Y, dir1X);
        double angle2 = Math.Atan2(dir2Y, dir2X);
        double bendAngle = angle2 - angle1;

        // Normalize angle
        while (bendAngle > Math.PI) bendAngle -= 2 * Math.PI;
        while (bendAngle < -Math.PI) bendAngle += 2 * Math.PI;

        // Calculate offset based on which side is the inner radius
        double innerRadius = bendRadius + halfWidth; // Distance from centerline to inner edge + bend radius

        // For the bend, we need to offset the start point of the new segment
        // The offset distance depends on the bend angle and inner radius
        double offset = innerRadius * Math.Tan(Math.Abs(bendAngle) / 2.0);

        // Move p2 along the direction of the new segment by the offset
        Point2D adjustedP2 = new Point2D(
            p2.X + dir2X * offset,
            p2.Y + dir2Y * offset
        );

        return adjustedP2;
    }

    private void CancelDrawing()
    {
        _isDrawing = false;
        _currentPoints.Clear();
        _segmentsForcedToMinimum.Clear();

        // Re-enable Tab navigation after drawing is cancelled
        KeyboardNavigation.SetTabNavigation(drawingCanvas, KeyboardNavigationMode.Continue);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);

        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }
        if (_previewPoint != null)
        {
            drawingCanvas.Children.Remove(_previewPoint);
            _previewPoint = null;
        }
        if (_previewLine != null)
        {
            drawingCanvas.Children.Remove(_previewLine);
            _previewLine = null;
        }
        if (_previewArc != null)
        {
            drawingCanvas.Children.Remove(_previewArc);
            _previewArc = null;
        }
        foreach (var marker in _previewMarkers)
        {
            drawingCanvas.Children.Remove(marker);
        }
        _previewMarkers.Clear();
        UpdateStatusBar("Drawing cancelled");
    }

    private Point2D GetCanvasMousePosition(System.Windows.Input.MouseEventArgs e)
    {
        // Get position in canvas coordinate space
        // When using RenderTransform on canvas, GetPosition returns coordinates
        // in the element's coordinate space, which is exactly what we need
        var point = e.GetPosition(drawingCanvas);
        return new Point2D(point.X, point.Y);
    }

    private double SnapAngle(double angle)
    {
        const double snapAngleDeg = 10.0;
        const double maxAngleDeg = 90.0;
        double angleDeg = angle * 180.0 / Math.PI;

        // Limit angle to ±90 degrees (limit angle between consecutive segments)
        // We need to calculate the relative angle from the previous segment
        if (_currentPoints.Count >= 1)
        {
            // Get the angle of the previous segment (if it exists)
            double prevAngleRad = 0;
            if (_currentPoints.Count >= 2)
            {
                var prevStart = _currentPoints[_currentPoints.Count - 2];
                var prevEnd = _currentPoints[_currentPoints.Count - 1];
                prevAngleRad = Math.Atan2(prevEnd.Y - prevStart.Y, prevEnd.X - prevStart.X);
            }

            // Calculate relative angle between segments
            double relativeAngleRad = angle - prevAngleRad;
            double relativeAngleDeg = relativeAngleRad * 180.0 / Math.PI;

            // Normalize to -180 to 180
            while (relativeAngleDeg > 180) relativeAngleDeg -= 360;
            while (relativeAngleDeg < -180) relativeAngleDeg += 360;

            // Clamp to ±90 degrees
            if (relativeAngleDeg > maxAngleDeg)
            {
                relativeAngleDeg = maxAngleDeg;
            }
            else if (relativeAngleDeg < -maxAngleDeg)
            {
                relativeAngleDeg = -maxAngleDeg;
            }

            // Convert back to absolute angle
            angle = prevAngleRad + (relativeAngleDeg * Math.PI / 180.0);
            angleDeg = angle * 180.0 / Math.PI;
        }

        // Check for horizontal snap (0° from the right or 180° from the left)
        if (Math.Abs(angleDeg) <= snapAngleDeg)
        {
            return 0;  // Snap to 0° (right)
        }
        else if (Math.Abs(angleDeg - 180) <= snapAngleDeg || Math.Abs(angleDeg + 180) <= snapAngleDeg)
        {
            return Math.PI;  // Snap to 180° (left)
        }
        // Check for vertical snap (90° down or -90° up)
        else if (Math.Abs(angleDeg - 90) <= snapAngleDeg)
        {
            return Math.PI / 2;  // Snap to 90° (down)
        }
        else if (Math.Abs(angleDeg + 90) <= snapAngleDeg)
        {
            return -Math.PI / 2;  // Snap to -90° (up)
        }

        return angle;  // No snap, return original angle
    }

    // Canvas Event Handlers
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;

        // Don't interfere with panning
        if (_isPanning) return;

        // If we're waiting for length input AND we have at least 2 points already, commit the edit
        // For the first segment (0 or 1 points), allow clicking to place points
        if (_waitingForLengthInput && _currentPoints.Count >= 2)
        {
            // Commit the current edit in the DataGrid (same as pressing Enter)
            dgSegments.CommitEdit();
            dgSegments.CommitEdit(); // Need to call twice: once for cell, once for row
            return;
        }

        // Get the canvas mouse position
        var pt = GetCanvasMousePosition(e);

        // Apply angle snapping if we have at least one existing point
        if (_currentPoints.Count >= 1)
        {
            var lastPoint = _currentPoints[_currentPoints.Count - 1];
            double dx = pt.X - lastPoint.X;
            double dy = pt.Y - lastPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx);

            // Snap the angle
            angle = SnapAngle(angle);

            // Recalculate the point position with snapped angle
            pt = new Point2D(
                lastPoint.X + length * Math.Cos(angle),
                lastPoint.Y + length * Math.Sin(angle)
            );
        }

        // Remove preview point and line since we're locking the position
        if (_previewPoint != null)
        {
            drawingCanvas.Children.Remove(_previewPoint);
            _previewPoint = null;
        }
        if (_previewLine != null)
        {
            drawingCanvas.Children.Remove(_previewLine);
            _previewLine = null;
        }

        // Add the point
        _currentPoints.Add(pt);

        // Draw the blue point at the clicked location
        DrawPoint(pt, Brushes.Blue);

        // Redraw all centerlines with rounded corners
        RedrawCenterlines();

        // If we have at least 2 points, calculate and add segment info to the DataGrid
        if (_currentPoints.Count >= 2)
        {
            var lastIdx = _currentPoints.Count - 1;
            var segmentStart = _currentPoints[lastIdx - 1];
            var segmentEnd = _currentPoints[lastIdx];
            double segmentLength = segmentStart.DistanceTo(segmentEnd);
            double segmentAngle = 0;

            // For the first segment, angle is 0
            if (_currentPoints.Count == 2)
            {
                segmentAngle = 0;
            }
            else
            {
                // Calculate angle between this segment and the previous one
                var prevStart = _currentPoints[lastIdx - 2];
                var prevEnd = _currentPoints[lastIdx - 1];

                // Vector of previous segment
                double prevDx = prevEnd.X - prevStart.X;
                double prevDy = prevEnd.Y - prevStart.Y;
                double prevAngle = Math.Atan2(prevDy, prevDx);

                // Vector of current segment
                double currDx = segmentEnd.X - segmentStart.X;
                double currDy = segmentEnd.Y - segmentStart.Y;
                double currAngle = Math.Atan2(currDy, currDx);

                // Angle between segments (in degrees)
                segmentAngle = (currAngle - prevAngle) * 180.0 / Math.PI;

                // Normalize to -180 to 180
                while (segmentAngle > 180) segmentAngle -= 360;
                while (segmentAngle < -180) segmentAngle += 360;
            }

            AddSegment(segmentAngle, segmentLength);

            // Automatically save or update the busbar after each segment
            SaveOrUpdateCurrentBusbar();
        }

        UpdateStatusBar($"Point {_currentPoints.Count} added at ({pt.X:F0}, {pt.Y:F0})");
    }

    private void DataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is System.Windows.Controls.TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();

            // Only attach handler if this is a different textbox
            if (_currentEditingTextBox != textBox)
            {
                // Remove old handler if it exists
                if (_currentEditingTextBox != null)
                {
                    _currentEditingTextBox.TextChanged -= LengthTextBox_TextChanged;
                }

                _currentEditingTextBox = textBox;
                textBox.TextChanged += LengthTextBox_TextChanged;
            }
        }
    }

    private void LengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Update preview point and line position based on typed length
        if (!_waitingForLengthInput) return;
        if (_currentPoints.Count == 0) return;

        var textBox = sender as System.Windows.Controls.TextBox;
        if (textBox == null) return;

        // Remove any existing preview polygon
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }

        // Try to parse the length value
        if (double.TryParse(textBox.Text, out double typedLength) && typedLength > 0)
        {
            // Calculate new preview point position with the typed length
            var lastPoint = _currentPoints[_currentPoints.Count - 1];
            var previewEndPoint = new Point2D(
                lastPoint.X + typedLength * Math.Cos(_pendingAngle),
                lastPoint.Y + typedLength * Math.Sin(_pendingAngle)
            );

            // Remove old preview point, line, arc, and markers
            if (_previewPoint != null)
            {
                drawingCanvas.Children.Remove(_previewPoint);
            }
            if (_previewLine != null)
            {
                drawingCanvas.Children.Remove(_previewLine);
                _previewLine = null;
            }
            if (_previewArc != null)
            {
                drawingCanvas.Children.Remove(_previewArc);
                _previewArc = null;
            }
            foreach (var marker in _previewMarkers)
            {
                drawingCanvas.Children.Remove(marker);
            }
            _previewMarkers.Clear();

            // Calculate preview line start position (may be trimmed if there's a previous segment)
            Point2D previewLineStart = lastPoint;
            double minimumLength = 0;

            if (_currentPoints.Count >= 2)
            {
                // Trim the start of the preview line to account for the bend radius
                double thickness = _currentProject.MaterialSettings.Thickness;
                double toolRadius = _currentProject.MaterialSettings.BendToolRadius;
                double bendRadius = toolRadius + (thickness / 2.0);

                var prevPoint = _currentPoints[_currentPoints.Count - 2];

                // Calculate the minimum required length (trim distance)
                // Direction vectors
                double dx1 = lastPoint.X - prevPoint.X;
                double dy1 = lastPoint.Y - prevPoint.Y;
                double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

                double dx2 = previewEndPoint.X - lastPoint.X;
                double dy2 = previewEndPoint.Y - lastPoint.Y;
                double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

                if (len1 >= 0.01 && len2 >= 0.01)
                {
                    // Unit vectors
                    double u1x = dx1 / len1;
                    double u1y = dy1 / len1;
                    double u2x = dx2 / len2;
                    double u2y = dy2 / len2;

                    // Calculate the angle between segments using dot product
                    double dot = u1x * u2x + u1y * u2y;
                    double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));

                    if (Math.Abs(angleRad) >= 0.001)
                    {
                        // Tangent length for the fillet
                        minimumLength = bendRadius * Math.Tan(angleRad / 2.0);
                    }
                }

                // If typed length is less than minimum required, don't show preview
                if (typedLength < minimumLength)
                {
                    return;
                }

                previewLineStart = TrimLineStart(prevPoint, lastPoint, previewEndPoint, bendRadius);

                // Temporarily redraw all confirmed segments with trimming for the preview
                // Remove all existing lines and arcs (but keep the blue points)
                for (int i = _currentShapes.Count - 1; i >= 0; i--)
                {
                    if (_currentShapes[i] is Line || _currentShapes[i] is System.Windows.Shapes.Path)
                    {
                        drawingCanvas.Children.Remove(_currentShapes[i]);
                        _currentShapes.RemoveAt(i);
                    }
                }

                // Redraw all confirmed segments with proper trimming considering the preview endpoint
                for (int i = 0; i < _currentPoints.Count - 1; i++)
                {
                    var p1 = _currentPoints[i];
                    var p2 = _currentPoints[i + 1];

                    Point2D segStart = p1;
                    Point2D segEnd = p2;

                    // Trim start if there's a previous segment
                    if (i > 0)
                    {
                        var p0 = _currentPoints[i - 1];
                        segStart = TrimLineStart(p0, p1, p2, bendRadius);
                    }

                    // Trim end if this is the last confirmed segment (where we're adding the preview)
                    if (i == _currentPoints.Count - 2)
                    {
                        // Use the preview endpoint to calculate the trim
                        segEnd = TrimLineEnd(p1, p2, previewEndPoint, bendRadius);
                    }
                    else if (i < _currentPoints.Count - 2)
                    {
                        // There's a next confirmed segment
                        var p3 = _currentPoints[i + 2];
                        segEnd = TrimLineEnd(p1, p2, p3, bendRadius);
                    }

                    // Draw the segment
                    var line = new Line
                    {
                        X1 = segStart.X,
                        Y1 = segStart.Y,
                        X2 = segEnd.X,
                        Y2 = segEnd.Y,
                        Stroke = Brushes.LightGray,
                        StrokeThickness = 1
                    };
                    drawingCanvas.Children.Add(line);
                    _currentShapes.Add(line);

                    // Draw edge lines parallel to the centerline
                    DrawEdgeLines(segStart, segEnd, false);

                    // Draw arc if there's a next segment
                    if (i < _currentPoints.Count - 2)
                    {
                        var p3 = _currentPoints[i + 2];
                        DrawBendArc(p1, p2, p3, bendRadius);
                    }
                }

                // Draw preview arc at the previous bend point
                DrawPreviewArc(prevPoint, lastPoint, previewEndPoint, bendRadius);
            }

            // Draw preview line from trimmed start to calculated end point
            _previewLine = new Line
            {
                X1 = previewLineStart.X,
                Y1 = previewLineStart.Y,
                X2 = previewEndPoint.X,
                Y2 = previewEndPoint.Y,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            drawingCanvas.Children.Add(_previewLine);

            // Draw preview edge lines
            DrawEdgeLines(previewLineStart, previewEndPoint, true);

            // Draw new preview point at calculated position
            _previewPoint = new Ellipse
            {
                Width = 2,
                Height = 2,
                Fill = Brushes.Blue,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_previewPoint, previewEndPoint.X - 1);
            Canvas.SetTop(_previewPoint, previewEndPoint.Y - 1);
            drawingCanvas.Children.Add(_previewPoint);
        }
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_waitingForLengthInput) return;

        // If user cancels (moves mouse away), reset the editing flag so timer can work again
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            _isEditingLength = false;
            return;
        }

        if (e.EditAction != DataGridEditAction.Commit) return;

        // Get the edited length value
        var textBox = e.EditingElement as System.Windows.Controls.TextBox;
        if (textBox == null) return;

        if (!double.TryParse(textBox.Text, out double desiredLength) || desiredLength <= 0)
        {
            UpdateStatusBar("Invalid length. Segment cancelled.");
            _waitingForLengthInput = false;
            _isEditingLength = false;

            // Restore the DataGrid to show only confirmed segments
            dgSegments.ItemsSource = null;
            dgSegments.ItemsSource = _currentSegments;
            return;
        }

        // Apply minimum length constraint of 50mm
        const double minimumSegmentLength = 50.0;
        bool wasForcedToMinimum = false;
        if (desiredLength < minimumSegmentLength)
        {
            desiredLength = minimumSegmentLength;
            wasForcedToMinimum = true;
            UpdateStatusBar($"Length adjusted to minimum: {minimumSegmentLength}mm");
        }

        // Track if this segment was forced to minimum
        _segmentsForcedToMinimum.Add(wasForcedToMinimum);

        // Calculate the new end point with the desired length
        var start = _currentPoints[_currentPoints.Count - 1];
        var pt = new Point2D(
            start.X + desiredLength * Math.Cos(_pendingAngle),
            start.Y + desiredLength * Math.Sin(_pendingAngle)
        );

        _currentPoints.Add(pt);

        // Remove preview shapes
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }
        if (_previewPoint != null)
        {
            drawingCanvas.Children.Remove(_previewPoint);
            _previewPoint = null;
        }
        if (_previewLine != null)
        {
            drawingCanvas.Children.Remove(_previewLine);
            _previewLine = null;
        }

        // Draw the end point at the new position (start point was already drawn on click)
        DrawPoint(pt, Brushes.Blue);

        // Redraw all centerlines with rounded corners
        RedrawCenterlines();

        // Calculate segment info for the list
        var lastIdx = _currentPoints.Count - 1;
        var segmentStart = _currentPoints[lastIdx - 1];
        var segmentEnd = _currentPoints[lastIdx];
        double segmentLength = segmentStart.DistanceTo(segmentEnd);
        double segmentAngle = 0;

        // For the first segment, angle is 0
        if (_currentPoints.Count == 2)
        {
            segmentAngle = 0;
        }
        else
        {
            // Calculate angle between this segment and the previous one
            var prevStart = _currentPoints[lastIdx - 2];
            var prevEnd = _currentPoints[lastIdx - 1];

            // Vector of previous segment
            double prevDx = prevEnd.X - prevStart.X;
            double prevDy = prevEnd.Y - prevStart.Y;
            double prevAngle = Math.Atan2(prevDy, prevDx);

            // Vector of current segment
            double currDx = segmentEnd.X - segmentStart.X;
            double currDy = segmentEnd.Y - segmentStart.Y;
            double currAngle = Math.Atan2(currDy, currDx);

            // Angle between segments (in degrees)
            segmentAngle = (currAngle - prevAngle) * 180.0 / Math.PI;

            // Normalize to -180 to 180
            while (segmentAngle > 180) segmentAngle -= 360;
            while (segmentAngle < -180) segmentAngle += 360;
        }

        AddSegment(segmentAngle, segmentLength);

        // Automatically save or update the busbar after each segment
        SaveOrUpdateCurrentBusbar();

        // Reset waiting state for next segment
        _waitingForLengthInput = false;
        _isEditingLength = false;

        UpdateStatusBar($"Segment added. Move mouse for next segment or press Esc to finish.");
    }

    private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Handle panning
        if (_isPanning)
        {
            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _lastPanPoint;

            _translateTransform.X += delta.X;
            _translateTransform.Y += delta.Y;

            _lastPanPoint = currentPoint;
            e.Handled = true;
            return;
        }

        // Handle live preview drawing
        if (!_isDrawing) return;

        // Remove old preview
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }

        // Get the canvas mouse position
        var currentPt = GetCanvasMousePosition(e);

        // Remove old preview point, line, arc, and markers
        if (_previewPoint != null)
        {
            drawingCanvas.Children.Remove(_previewPoint);
        }
        if (_previewLine != null)
        {
            drawingCanvas.Children.Remove(_previewLine);
            _previewLine = null;
        }
        if (_previewArc != null)
        {
            drawingCanvas.Children.Remove(_previewArc);
            _previewArc = null;
        }
        foreach (var marker in _previewMarkers)
        {
            drawingCanvas.Children.Remove(marker);
        }
        _previewMarkers.Clear();

        // Need at least one point to calculate preview position
        if (_currentPoints.Count == 0)
        {
            // No points yet, show preview at cursor location
            _previewPoint = new Ellipse
            {
                Width = 2,
                Height = 2,
                Fill = Brushes.Blue,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                IsHitTestVisible = false  // Don't block mouse events
            };
            Canvas.SetLeft(_previewPoint, currentPt.X - 1);
            Canvas.SetTop(_previewPoint, currentPt.Y - 1);
            drawingCanvas.Children.Add(_previewPoint);
            return;
        }

        var lastPoint = _currentPoints[_currentPoints.Count - 1];

        // Calculate preview measurements
        const double minLength = 50.0;  // Minimum segment length

        double dx = currentPt.X - lastPoint.X;
        double dy = currentPt.Y - lastPoint.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.1) return; // Too short to draw

        // Enforce minimum length of 50mm
        if (length < minLength)
        {
            length = minLength;
        }

        // Normalize direction and apply length
        double angle = Math.Atan2(dy, dx);

        // Snap to horizontal or vertical if within 10 degrees
        angle = SnapAngle(angle);

        Point2D endPoint = new Point2D(
            lastPoint.X + length * Math.Cos(angle),
            lastPoint.Y + length * Math.Sin(angle)
        );

        // Store the pending angle for when user confirms
        _pendingAngle = angle;

        // Calculate preview line start position (may be trimmed if there's a previous segment)
        Point2D previewLineStart = lastPoint;
        if (_currentPoints.Count >= 2)
        {
            // Trim the start of the preview line to account for the bend radius
            double thickness = _currentProject.MaterialSettings.Thickness;
            double toolRadius = _currentProject.MaterialSettings.BendToolRadius;
            double bendRadius = toolRadius + (thickness / 2.0);

            var prevPoint = _currentPoints[_currentPoints.Count - 2];
            previewLineStart = TrimLineStart(prevPoint, lastPoint, endPoint, bendRadius);

            // Temporarily redraw all confirmed segments with trimming for the preview
            // Remove all existing lines and arcs (but keep the blue points)
            for (int i = _currentShapes.Count - 1; i >= 0; i--)
            {
                if (_currentShapes[i] is Line || _currentShapes[i] is System.Windows.Shapes.Path)
                {
                    drawingCanvas.Children.Remove(_currentShapes[i]);
                    _currentShapes.RemoveAt(i);
                }
            }

            // Redraw all confirmed segments with proper trimming considering the preview endpoint
            for (int i = 0; i < _currentPoints.Count - 1; i++)
            {
                var p1 = _currentPoints[i];
                var p2 = _currentPoints[i + 1];

                Point2D segStart = p1;
                Point2D segEnd = p2;

                // Trim start if there's a previous segment
                if (i > 0)
                {
                    var p0 = _currentPoints[i - 1];
                    segStart = TrimLineStart(p0, p1, p2, bendRadius);
                }

                // Trim end if this is the last confirmed segment (where we're adding the preview)
                if (i == _currentPoints.Count - 2)
                {
                    // Use the preview endpoint to calculate the trim
                    segEnd = TrimLineEnd(p1, p2, endPoint, bendRadius);
                }
                else if (i < _currentPoints.Count - 2)
                {
                    // There's a next confirmed segment
                    var p3 = _currentPoints[i + 2];
                    segEnd = TrimLineEnd(p1, p2, p3, bendRadius);
                }

                // Draw the segment
                var line = new Line
                {
                    X1 = segStart.X,
                    Y1 = segStart.Y,
                    X2 = segEnd.X,
                    Y2 = segEnd.Y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1
                };
                drawingCanvas.Children.Add(line);
                _currentShapes.Add(line);

                // Draw edge lines parallel to the centerline
                DrawEdgeLines(segStart, segEnd, false);

                // Draw arc if there's a next segment
                if (i < _currentPoints.Count - 2)
                {
                    var p3 = _currentPoints[i + 2];
                    DrawBendArc(p1, p2, p3, bendRadius);
                }
            }

            // Draw preview arc at the previous bend point
            DrawPreviewArc(prevPoint, lastPoint, endPoint, bendRadius);
        }

        // Draw preview line from trimmed start to calculated end point
        _previewLine = new Line
        {
            X1 = previewLineStart.X,
            Y1 = previewLineStart.Y,
            X2 = endPoint.X,
            Y2 = endPoint.Y,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(_previewLine);

        // Draw preview edge lines
        DrawEdgeLines(previewLineStart, endPoint, true);

        // Draw preview point at the snapped/calculated position
        _previewPoint = new Ellipse
        {
            Width = 2,
            Height = 2,
            Fill = Brushes.Blue,
            Stroke = Brushes.Black,
            StrokeThickness = 0.5,
            IsHitTestVisible = false  // Don't block mouse events
        };
        Canvas.SetLeft(_previewPoint, endPoint.X - 1);
        Canvas.SetTop(_previewPoint, endPoint.Y - 1);
        drawingCanvas.Children.Add(_previewPoint);

        // Update measurements in DataGrid (no visual preview)
        UpdateLivePreviewMeasurements(lastPoint, endPoint);

        // After first point is placed, start/restart timer to detect when mouse stops
        if (_currentPoints.Count >= 1)
        {
            if (!_waitingForLengthInput)
            {
                _waitingForLengthInput = true;
            }

            // Reset editing flag when mouse moves (allow re-highlighting on next stop)
            if (_isEditingLength)
            {
                _isEditingLength = false;
                // Cancel any active edit in the DataGrid
                dgSegments.CancelEdit();
            }

            // Initialize timer if needed
            if (_mouseStopTimer == null)
            {
                _mouseStopTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200) // 200ms delay
                };
                _mouseStopTimer.Tick += MouseStopTimer_Tick;
            }

            // Restart the timer - if mouse keeps moving, it keeps resetting
            _mouseStopTimer.Stop();
            _mouseStopTimer.Start();
        }
    }

    private void MouseStopTimer_Tick(object? sender, EventArgs e)
    {
        _mouseStopTimer?.Stop();

        if (!_waitingForLengthInput) return;

        // Focus the last row's Length cell for editing
        if (dgSegments.Items.Count > 0)
        {
            int lastRowIndex = dgSegments.Items.Count - 1;
            dgSegments.CurrentCell = new DataGridCellInfo(dgSegments.Items[lastRowIndex], dgSegments.Columns[1]);
            dgSegments.Focus();
            dgSegments.BeginEdit();
            _isEditingLength = true;
        }

        UpdateStatusBar("Type length and press Enter to confirm segment");
    }

    private void UpdateLivePreviewMeasurements(Point2D start, Point2D end, string? lengthOverride = null)
    {
        if (_currentPoints.Count == 0) return;

        // Calculate preview segment length
        double previewLength = start.DistanceTo(end);
        double previewAngle = 0;

        // For the first segment preview, angle is 0
        if (_currentPoints.Count == 1)
        {
            previewAngle = 0;
        }
        else
        {
            // Calculate angle between the last segment and the preview segment
            var prevStart = _currentPoints[_currentPoints.Count - 2];
            var prevEnd = _currentPoints[_currentPoints.Count - 1];

            double prevDx = prevEnd.X - prevStart.X;
            double prevDy = prevEnd.Y - prevStart.Y;
            double prevSegAngle = Math.Atan2(prevDy, prevDx);

            double currDx = end.X - start.X;
            double currDy = end.Y - start.Y;
            double currSegAngle = Math.Atan2(currDy, currDx);

            previewAngle = (currSegAngle - prevSegAngle) * 180.0 / Math.PI;

            // Normalize to -180 to 180
            while (previewAngle > 180) previewAngle -= 360;
            while (previewAngle < -180) previewAngle += 360;
        }

        // Create a temporary list with current segments + preview
        var tempSegments = new List<SegmentInfo>(_currentSegments);
        tempSegments.Add(new SegmentInfo
        {
            Angle = previewAngle.ToString("F1"),
            Length = lengthOverride ?? previewLength.ToString("F1")
        });

        // Update the DataGrid with the preview
        dgSegments.ItemsSource = null;
        dgSegments.ItemsSource = tempSegments;
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
            // Cancel any pending input and finish drawing
            _waitingForLengthInput = false;
            FinishDrawing();
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Zoom in/out with mouse wheel
        // Get position in canvas coordinate space (already transformed)
        var canvasPoint = e.GetPosition(drawingCanvas);

        // Calculate zoom factor (slower for more control)
        double zoomFactor = e.Delta > 0 ? 1.05 : 0.95;

        // Store the canvas point we want to keep fixed
        double fixedX = canvasPoint.X;
        double fixedY = canvasPoint.Y;

        // Apply zoom
        _scaleTransform.ScaleX *= zoomFactor;
        _scaleTransform.ScaleY *= zoomFactor;

        // Calculate where that point would be in screen space after zoom
        // We need to adjust the translation so the point stays under the cursor
        // New screen position = scale * canvas position + translate
        // We want: screen position to remain the same
        // So: translate_new = screen_position - scale_new * canvas_position

        // Get the screen position (relative to canvas parent)
        var screenPoint = e.GetPosition((IInputElement)drawingCanvas.Parent);
        _translateTransform.X = screenPoint.X - fixedX * _scaleTransform.ScaleX;
        _translateTransform.Y = screenPoint.Y - fixedY * _scaleTransform.ScaleY;

        e.Handled = true;
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle mouse button, Ctrl+Left button, or Hand tool active for panning
        if (e.MiddleButton == MouseButtonState.Pressed ||
            (e.LeftButton == MouseButtonState.Pressed && Keyboard.Modifiers == ModifierKeys.Control) ||
            (e.LeftButton == MouseButtonState.Pressed && _handToolActive))
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            drawingCanvas.Cursor = System.Windows.Input.Cursors.Hand;
            drawingCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            // Restore cursor based on hand tool state
            drawingCanvas.Cursor = _handToolActive ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            drawingCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void AutoScaleToFitBusbar()
    {
        if (_currentPoints.Count < 2) return;

        // Calculate bounds of all current points
        double minX = _currentPoints.Min(p => p.X);
        double maxX = _currentPoints.Max(p => p.X);
        double minY = _currentPoints.Min(p => p.Y);
        double maxY = _currentPoints.Max(p => p.Y);

        // Add margin for the 10mm busbar width and some padding
        const double margin = 50; // 50mm margin
        double busbarLeft = minX - margin;
        double busbarTop = minY - margin;
        double busbarRight = maxX + margin;
        double busbarBottom = maxY + margin;

        double totalWidth = busbarRight - busbarLeft;
        double totalHeight = busbarBottom - busbarTop;

        // Get canvas actual size
        double canvasWidth = drawingCanvas.ActualWidth > 0 ? drawingCanvas.ActualWidth : 800;
        double canvasHeight = drawingCanvas.ActualHeight > 0 ? drawingCanvas.ActualHeight : 600;

        // Calculate scale to fit with some padding
        double scaleX = (canvasWidth * 0.9) / totalWidth;
        double scaleY = (canvasHeight * 0.9) / totalHeight;
        double scale = Math.Min(scaleX, scaleY);

        // Apply scale
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;

        // Center the busbar in the canvas
        _translateTransform.X = (canvasWidth - totalWidth * scale) / 2 - (busbarLeft * scale);
        _translateTransform.Y = (canvasHeight - totalHeight * scale) / 2 - (busbarTop * scale);
    }

    private void DrawPoint(Point2D point, System.Windows.Media.Brush color, double radius = 1)
    {
        // Draw a simple circle at the point location
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = color,
            Stroke = Brushes.Black,
            StrokeThickness = 0.5
        };

        // Position the ellipse so its center is at the point
        Canvas.SetLeft(ellipse, point.X - radius);
        Canvas.SetTop(ellipse, point.Y - radius);

        drawingCanvas.Children.Add(ellipse);

        // Track shape if we're currently drawing
        if (_isDrawing)
        {
            _currentShapes.Add(ellipse);
        }
    }

    private void RedrawCenterlines()
    {
        // Remove all existing centerline shapes (lines and arcs)
        // Keep only the blue points
        for (int i = _currentShapes.Count - 1; i >= 0; i--)
        {
            if (_currentShapes[i] is Line || _currentShapes[i] is System.Windows.Shapes.Path)
            {
                drawingCanvas.Children.Remove(_currentShapes[i]);
                _currentShapes.RemoveAt(i);
            }
        }

        if (_currentPoints.Count < 2) return;

        // Get bend radius from material settings
        double thickness = _currentProject.MaterialSettings.Thickness;
        double toolRadius = _currentProject.MaterialSettings.BendToolRadius;
        double bendRadius = toolRadius + (thickness / 2.0);

        // Draw perpendicular marker at the start of the busbar (blue)
        if (_currentPoints.Count >= 2)
        {
            var firstPoint = _currentPoints[0];
            var secondPoint = _currentPoints[1];
            double startAngle = Math.Atan2(secondPoint.Y - firstPoint.Y, secondPoint.X - firstPoint.X);
            DrawPerpendicularMarker(firstPoint, startAngle, false, true);
        }

        // Draw centerlines with rounded corners
        for (int i = 0; i < _currentPoints.Count - 1; i++)
        {
            var p1 = _currentPoints[i];
            var p2 = _currentPoints[i + 1];

            Point2D lineStart = p1;
            Point2D lineEnd = p2;

            // Check if there's a previous segment (for start trim)
            if (i > 0)
            {
                var p0 = _currentPoints[i - 1];
                lineStart = TrimLineStart(p0, p1, p2, bendRadius);
            }

            // Check if there's a next segment (for end trim)
            if (i < _currentPoints.Count - 2)
            {
                var p3 = _currentPoints[i + 2];
                lineEnd = TrimLineEnd(p1, p2, p3, bendRadius);
            }

            // Draw the trimmed line segment (centerline)
            var line = new Line
            {
                X1 = lineStart.X,
                Y1 = lineStart.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                Stroke = Brushes.LightGray,
                StrokeThickness = 1
            };
            drawingCanvas.Children.Add(line);
            _currentShapes.Add(line);

            // Draw edge lines parallel to the centerline
            DrawEdgeLines(lineStart, lineEnd, false);

            // Draw arc at the end if there's a next segment
            if (i < _currentPoints.Count - 2)
            {
                var p3 = _currentPoints[i + 2];
                DrawBendArc(p1, p2, p3, bendRadius);
            }
        }

        // Draw perpendicular marker at the end of the busbar (blue)
        if (_currentPoints.Count >= 2)
        {
            var lastPoint = _currentPoints[_currentPoints.Count - 1];
            var secondLastPoint = _currentPoints[_currentPoints.Count - 2];
            double endAngle = Math.Atan2(lastPoint.Y - secondLastPoint.Y, lastPoint.X - secondLastPoint.X);
            DrawPerpendicularMarker(lastPoint, endAngle, false, true);
        }
    }

    private Point2D TrimLineStart(Point2D p0, Point2D p1, Point2D p2, double radius)
    {
        // Calculate the trim distance based on the bend angle
        // For a fillet, the tangent length = radius * tan(angle/2)

        // Direction vectors
        double dx1 = p1.X - p0.X;
        double dy1 = p1.Y - p0.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p2.X - p1.X;
        double dy2 = p2.Y - p1.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.01 || len2 < 0.01) return p1;

        // Unit vectors
        double u1x = dx1 / len1;
        double u1y = dy1 / len1;
        double u2x = dx2 / len2;
        double u2y = dy2 / len2;

        // Calculate the angle between segments using dot product
        double dot = u1x * u2x + u1y * u2y;
        double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))); // Clamp to avoid numerical errors

        if (Math.Abs(angleRad) < 0.001) return p1; // Nearly straight, no trim needed

        // Tangent length for the fillet
        double trimDistance = radius * Math.Tan(angleRad / 2.0);

        if (len2 < trimDistance) return p1; // Segment too short to trim

        // Unit vector from p1 to p2
        double ux = dx2 / len2;
        double uy = dy2 / len2;

        // Trim by calculated distance
        return new Point2D(p1.X + ux * trimDistance, p1.Y + uy * trimDistance);
    }

    private Point2D TrimLineEnd(Point2D p1, Point2D p2, Point2D p3, double radius)
    {
        // Calculate the trim distance based on the bend angle
        // For a fillet, the tangent length = radius * tan(angle/2)

        // Direction vectors
        double dx1 = p2.X - p1.X;
        double dy1 = p2.Y - p1.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p3.X - p2.X;
        double dy2 = p3.Y - p2.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.01 || len2 < 0.01) return p2;

        // Unit vectors
        double u1x = dx1 / len1;
        double u1y = dy1 / len1;
        double u2x = dx2 / len2;
        double u2y = dy2 / len2;

        // Calculate the angle between segments using dot product
        double dot = u1x * u2x + u1y * u2y;
        double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))); // Clamp to avoid numerical errors

        if (Math.Abs(angleRad) < 0.001) return p2; // Nearly straight, no trim needed

        // Tangent length for the fillet
        double trimDistance = radius * Math.Tan(angleRad / 2.0);

        if (len1 < trimDistance) return p2; // Segment too short to trim

        // Unit vector from p1 to p2
        double ux = dx1 / len1;
        double uy = dy1 / len1;

        // Trim by calculated distance from the end
        return new Point2D(p2.X - ux * trimDistance, p2.Y - uy * trimDistance);
    }

    private void DrawEdgeArcs(Point2D p1, Point2D p2, Point2D p3, double centerRadius, bool isPreview)
    {
        // Draw two offset arcs that connect the edge lines (5mm offset from centerline)
        // The edge arcs follow the same center as the centerline arc but with adjusted radii

        // Direction vectors
        double dx1 = p2.X - p1.X;
        double dy1 = p2.Y - p1.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p3.X - p2.X;
        double dy2 = p3.Y - p2.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.01 || len2 < 0.01) return;

        // Unit vectors
        double u1x = dx1 / len1;
        double u1y = dy1 / len1;
        double u2x = dx2 / len2;
        double u2y = dy2 / len2;

        // Calculate the angle between segments using dot product
        double dot = u1x * u2x + u1y * u2y;
        double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));

        if (Math.Abs(angleRad) < 0.001) return; // Nearly straight, no arc needed

        // Offset distance from centerline: 5mm on each side
        const double offset = 5.0;

        // Calculate the centerline arc's trim distance
        double centerTrimDistance = centerRadius * Math.Tan(angleRad / 2.0);

        // Calculate centerline arc endpoints
        Point2D centerArcStart = new Point2D(p2.X - u1x * centerTrimDistance, p2.Y - u1y * centerTrimDistance);
        Point2D centerArcEnd = new Point2D(p2.X + u2x * centerTrimDistance, p2.Y + u2y * centerTrimDistance);

        // Perpendicular vectors for offsetting (perpendicular to each segment direction)
        double perp1x = -u1y;
        double perp1y = u1x;
        double perp2x = -u2y;
        double perp2y = u2x;

        // Calculate edge arc start points (offset from centerline arc start)
        Point2D edge1ArcStart = new Point2D(centerArcStart.X + perp1x * offset, centerArcStart.Y + perp1y * offset);
        Point2D edge2ArcStart = new Point2D(centerArcStart.X - perp1x * offset, centerArcStart.Y - perp1y * offset);

        // Calculate edge arc end points (offset from centerline arc end)
        Point2D edge1ArcEnd = new Point2D(centerArcEnd.X + perp2x * offset, centerArcEnd.Y + perp2y * offset);
        Point2D edge2ArcEnd = new Point2D(centerArcEnd.X - perp2x * offset, centerArcEnd.Y - perp2y * offset);

        // Determine which edge is inner and which is outer based on turn direction
        double cross = u1x * u2y - u1y * u2x;
        bool sweepDirection = cross > 0;

        // For the edge arcs, we need to calculate the actual radii
        // The offset arcs have different radii than the centerline
        double innerRadius = centerRadius - offset;
        double outerRadius = centerRadius + offset;

        // Determine which edge gets which radius based on turn direction
        // When turning clockwise (cross > 0), edge1 is inner, edge2 is outer
        // When turning counterclockwise (cross < 0), edge1 is outer, edge2 is inner
        double edge1Radius = sweepDirection ? innerRadius : outerRadius;
        double edge2Radius = sweepDirection ? outerRadius : innerRadius;

        // Draw first edge arc
        var edge1PathFigure = new PathFigure
        {
            StartPoint = new System.Windows.Point(edge1ArcStart.X, edge1ArcStart.Y)
        };
        var edge1ArcSegment = new ArcSegment
        {
            Point = new System.Windows.Point(edge1ArcEnd.X, edge1ArcEnd.Y),
            Size = new System.Windows.Size(edge1Radius, edge1Radius),
            SweepDirection = sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsLargeArc = false
        };
        edge1PathFigure.Segments.Add(edge1ArcSegment);
        var edge1PathGeometry = new PathGeometry();
        edge1PathGeometry.Figures.Add(edge1PathFigure);

        var edge1Path = new System.Windows.Shapes.Path
        {
            Data = edge1PathGeometry,
            Stroke = Brushes.Blue,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(edge1Path);

        // Draw second edge arc
        var edge2PathFigure = new PathFigure
        {
            StartPoint = new System.Windows.Point(edge2ArcStart.X, edge2ArcStart.Y)
        };
        var edge2ArcSegment = new ArcSegment
        {
            Point = new System.Windows.Point(edge2ArcEnd.X, edge2ArcEnd.Y),
            Size = new System.Windows.Size(edge2Radius, edge2Radius),
            SweepDirection = sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsLargeArc = false
        };
        edge2PathFigure.Segments.Add(edge2ArcSegment);
        var edge2PathGeometry = new PathGeometry();
        edge2PathGeometry.Figures.Add(edge2PathFigure);

        var edge2Path = new System.Windows.Shapes.Path
        {
            Data = edge2PathGeometry,
            Stroke = Brushes.Blue,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(edge2Path);

        // Track shapes appropriately
        if (isPreview)
        {
            _previewMarkers.Add(edge1Path);
            _previewMarkers.Add(edge2Path);
        }
        else if (_isDrawing)
        {
            _currentShapes.Add(edge1Path);
            _currentShapes.Add(edge2Path);
        }
    }

    private void DrawEdgeLines(Point2D start, Point2D end, bool isPreview)
    {
        // Draw two parallel edge lines offset 5mm on each side of the centerline
        // Calculate the direction vector
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.01) return;

        // Unit direction vector
        double ux = dx / length;
        double uy = dy / length;

        // Perpendicular vector (90 degrees rotation)
        double perpX = -uy;
        double perpY = ux;

        // Offset distance: 5mm on each side
        const double offset = 5.0;

        // Calculate offset points for first edge line
        Point2D edge1Start = new Point2D(start.X + perpX * offset, start.Y + perpY * offset);
        Point2D edge1End = new Point2D(end.X + perpX * offset, end.Y + perpY * offset);

        // Calculate offset points for second edge line
        Point2D edge2Start = new Point2D(start.X - perpX * offset, start.Y - perpY * offset);
        Point2D edge2End = new Point2D(end.X - perpX * offset, end.Y - perpY * offset);

        // Draw first edge line
        var line1 = new Line
        {
            X1 = edge1Start.X,
            Y1 = edge1Start.Y,
            X2 = edge1End.X,
            Y2 = edge1End.Y,
            Stroke = Brushes.Blue,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(line1);

        // Draw second edge line
        var line2 = new Line
        {
            X1 = edge2Start.X,
            Y1 = edge2Start.Y,
            X2 = edge2End.X,
            Y2 = edge2End.Y,
            Stroke = Brushes.Blue,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(line2);

        // Track shapes appropriately
        if (isPreview)
        {
            _previewMarkers.Add(line1);
            _previewMarkers.Add(line2);
        }
        else if (_isDrawing)
        {
            _currentShapes.Add(line1);
            _currentShapes.Add(line2);
        }
    }

    private void DrawPerpendicularMarker(Point2D position, double directionAngle, bool isPreview, bool isEndPoint = false)
    {
        // Draw a perpendicular line at the given position
        // directionAngle is the angle of the centerline at this point (in radians)
        // The marker is perpendicular to this direction

        // Perpendicular angle is 90 degrees offset
        double perpAngle = directionAngle + Math.PI / 2.0;

        // Extension from centerline: 5mm on each side
        const double extension = 5.0;

        // Calculate the two endpoints of the perpendicular line
        double cos = Math.Cos(perpAngle);
        double sin = Math.Sin(perpAngle);

        Point2D lineStart = new Point2D(
            position.X - extension * cos,
            position.Y - extension * sin
        );

        Point2D lineEnd = new Point2D(
            position.X + extension * cos,
            position.Y + extension * sin
        );

        // Create the line
        var line = new Line
        {
            X1 = lineStart.X,
            Y1 = lineStart.Y,
            X2 = lineEnd.X,
            Y2 = lineEnd.Y,
            Stroke = isEndPoint ? Brushes.Blue : Brushes.DarkGray,
            StrokeThickness = isEndPoint ? 1 : 0.5,
            IsHitTestVisible = false
        };

        drawingCanvas.Children.Add(line);

        // Track shape appropriately
        if (isPreview)
        {
            _previewMarkers.Add(line);
        }
        else if (_isDrawing)
        {
            _currentShapes.Add(line);
        }
    }

    private void DrawPreviewArc(Point2D p1, Point2D p2, Point2D p3, double radius)
    {
        // Draw a preview arc at the bend point (same as DrawBendArc but stores in _previewArc)
        // p1 -> p2 is the previous segment
        // p2 -> p3 is the preview segment

        // Direction vectors
        double dx1 = p2.X - p1.X;
        double dy1 = p2.Y - p1.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p3.X - p2.X;
        double dy2 = p3.Y - p2.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.01 || len2 < 0.01) return;

        // Unit vectors
        double u1x = dx1 / len1;
        double u1y = dy1 / len1;
        double u2x = dx2 / len2;
        double u2y = dy2 / len2;

        // Calculate the angle between segments using dot product
        double dot = u1x * u2x + u1y * u2y;
        double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))); // Clamp to avoid numerical errors

        if (Math.Abs(angleRad) < 0.001) return; // Nearly straight, no arc needed

        // Tangent length for the fillet - this is how far back from p2 the arc starts/ends
        double trimDistance = radius * Math.Tan(angleRad / 2.0);

        // Arc start point (end of trimmed first segment)
        Point2D arcStart = new Point2D(p2.X - u1x * trimDistance, p2.Y - u1y * trimDistance);

        // Arc end point (start of trimmed second segment)
        Point2D arcEnd = new Point2D(p2.X + u2x * trimDistance, p2.Y + u2y * trimDistance);

        // Draw perpendicular markers at arc start and end
        double angle1 = Math.Atan2(dy1, dx1); // Angle of first segment
        double angle2 = Math.Atan2(dy2, dx2); // Angle of second segment
        DrawPerpendicularMarker(arcStart, angle1, true);
        DrawPerpendicularMarker(arcEnd, angle2, true);

        // Determine if this is a left or right turn using cross product
        double cross = u1x * u2y - u1y * u2x;
        bool isLargeArc = false;
        bool sweepDirection = cross > 0; // true = clockwise, false = counterclockwise

        // Create arc using PathGeometry
        var pathFigure = new PathFigure
        {
            StartPoint = new System.Windows.Point(arcStart.X, arcStart.Y)
        };

        var arcSegment = new ArcSegment
        {
            Point = new System.Windows.Point(arcEnd.X, arcEnd.Y),
            Size = new System.Windows.Size(radius, radius),
            SweepDirection = sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsLargeArc = isLargeArc
        };

        pathFigure.Segments.Add(arcSegment);

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);

        _previewArc = new System.Windows.Shapes.Path
        {
            Data = pathGeometry,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };

        drawingCanvas.Children.Add(_previewArc);

        // Draw edge arcs (offset arcs at inner and outer radii)
        DrawEdgeArcs(p1, p2, p3, radius, true);
    }

    private void DrawBendArc(Point2D p1, Point2D p2, Point2D p3, double radius)
    {
        // Calculate the arc that connects the two line segments
        // p1 -> p2 is the previous segment
        // p2 -> p3 is the next segment

        // Direction vectors
        double dx1 = p2.X - p1.X;
        double dy1 = p2.Y - p1.Y;
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

        double dx2 = p3.X - p2.X;
        double dy2 = p3.Y - p2.Y;
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.01 || len2 < 0.01) return;

        // Unit vectors
        double u1x = dx1 / len1;
        double u1y = dy1 / len1;
        double u2x = dx2 / len2;
        double u2y = dy2 / len2;

        // Calculate the angle between segments using dot product
        double dot = u1x * u2x + u1y * u2y;
        double angleRad = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))); // Clamp to avoid numerical errors

        if (Math.Abs(angleRad) < 0.001) return; // Nearly straight, no arc needed

        // Tangent length for the fillet - this is how far back from p2 the arc starts/ends
        double trimDistance = radius * Math.Tan(angleRad / 2.0);

        // Arc start point (end of trimmed first segment)
        Point2D arcStart = new Point2D(p2.X - u1x * trimDistance, p2.Y - u1y * trimDistance);

        // Arc end point (start of trimmed second segment)
        Point2D arcEnd = new Point2D(p2.X + u2x * trimDistance, p2.Y + u2y * trimDistance);

        // Draw perpendicular markers at arc start and end
        double angle1 = Math.Atan2(dy1, dx1); // Angle of first segment
        double angle2 = Math.Atan2(dy2, dx2); // Angle of second segment
        DrawPerpendicularMarker(arcStart, angle1, false);
        DrawPerpendicularMarker(arcEnd, angle2, false);

        // Determine if this is a left or right turn using cross product
        double cross = u1x * u2y - u1y * u2x;
        bool isLargeArc = false;
        bool sweepDirection = cross > 0; // true = clockwise, false = counterclockwise

        // Create arc using PathGeometry
        var pathFigure = new PathFigure
        {
            StartPoint = new System.Windows.Point(arcStart.X, arcStart.Y)
        };

        var arcSegment = new ArcSegment
        {
            Point = new System.Windows.Point(arcEnd.X, arcEnd.Y),
            Size = new System.Windows.Size(radius, radius),
            SweepDirection = sweepDirection ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsLargeArc = isLargeArc
        };

        pathFigure.Segments.Add(arcSegment);

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);

        var path = new System.Windows.Shapes.Path
        {
            Data = pathGeometry,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1
        };

        drawingCanvas.Children.Add(path);
        _currentShapes.Add(path);

        // Draw edge arcs (offset arcs at inner and outer radii)
        DrawEdgeArcs(p1, p2, p3, radius, false);
    }

    private void DrawLine(Point2D start, Point2D end, System.Windows.Media.Brush color, double thickness)
    {
        // This method is no longer used for centerlines during drawing
        // It's kept for compatibility with RedrawCanvas for finished busbars
        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1
        };

        drawingCanvas.Children.Add(line);

        // Track shape if we're currently drawing
        if (_isDrawing)
        {
            _currentShapes.Add(line);
        }

        // Draw the end point (start point already exists from previous segment)
        DrawPoint(end, color);
    }

    private void RedrawCanvas()
    {
        drawingCanvas.Children.Clear();

        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null) return;

        foreach (var busbar in activeLayer.Busbars)
        {
            var color = busbar.IsValid ? Brushes.Blue : Brushes.Red;

            foreach (var segment in busbar.Segments)
            {
                DrawLine(segment.StartPoint, segment.EndPoint, color, 1);
            }
        }
    }

    // Keyboard Shortcuts
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.D && !_isDrawing)
        {
            StartDrawing();
        }
        else if (e.Key == Key.Tab && _isDrawing && !_waitingForLengthInput)
        {
            // Prevent Tab from changing focus during drawing mode (unless editing in DataGrid)
            e.Handled = true;
            drawingCanvas.Focus();
        }
        else if (e.Key == Key.Escape && _isDrawing)
        {
            // Cancel any active DataGrid edit first
            if (_isEditingLength)
            {
                dgSegments.CancelEdit();
                _isEditingLength = false;
            }

            // Cancel any pending input and finish drawing (same as right-click)
            _waitingForLengthInput = false;
            _mouseStopTimer?.Stop();
            FinishDrawing();
            e.Handled = true;
        }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus)
        {
            ZoomAtCenter(1.1);
        }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
        {
            ZoomAtCenter(1.0 / 1.1);
        }
        else if (e.Key == Key.F)
        {
            AutoScaleToFitAll();
        }
        else if (e.Key == Key.H)
        {
            Hand_Click(this, new RoutedEventArgs());
        }
    }

    // Settings Changed Handlers
    private void BusbarWidth_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Check if project is initialized (this event fires during InitializeComponent)
        if (_currentProject == null) return;

        if (cmbBusbarWidth.SelectedItem is ComboBoxItem item)
        {
            _currentProject.MaterialSettings.BusbarWidth = double.Parse(item.Content.ToString());
        }
    }

    private void Busbar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstBusbars.SelectedIndex < 0 || lstBusbars.SelectedIndex >= _savedBusbars.Count)
        {
            return;
        }

        // Get the selected busbar
        var selectedBusbar = _savedBusbars[lstBusbars.SelectedIndex];

        // Display its name in the textbox
        txtBusbarName.Text = selectedBusbar.Name;

        // Display its segments in the right panel DataGrid
        dgSegments.ItemsSource = null;
        dgSegments.ItemsSource = selectedBusbar.Segments;

        UpdateStatusBar($"Selected busbar: {selectedBusbar.Name} ({selectedBusbar.Segments.Count} segments)");
    }
}