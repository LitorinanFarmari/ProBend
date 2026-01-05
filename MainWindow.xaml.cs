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
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Project _currentProject;
    private bool _isDrawing = false;
    private List<Point2D> _currentPoints = new List<Point2D>();
    private Polygon? _previewPolygon = null;
    private TransformGroup _canvasTransform;
    private ScaleTransform _scaleTransform;
    private TranslateTransform _translateTransform;
    private bool _isPanning = false;
    private System.Windows.Point _lastPanPoint;
    private bool _handToolActive = false;

    public MainWindow()
    {
        InitializeComponent();
        InitializeCanvasTransform();
        InitializeProject();
        this.KeyDown += Window_KeyDown;
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

    private void SetInitialScale()
    {
        // Calculate scale so that 200mm fits comfortably in the canvas width
        double targetVisibleMm = 200.0;
        double canvasWidth = drawingCanvas.ActualWidth > 0 ? drawingCanvas.ActualWidth : 800; // Fallback width
        double initialScale = (canvasWidth * 0.8) / targetVisibleMm; // 80% of canvas width

        _scaleTransform.ScaleX = initialScale;
        _scaleTransform.ScaleY = initialScale;

        // Center the origin
        _translateTransform.X = canvasWidth * 0.1; // 10% margin from left
        _translateTransform.Y = 100; // Some margin from top
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

        foreach (var busbar in activeLayer.Busbars)
        {
            var result = ValidationEngine.ValidateBusbar(busbar);
            if (!result.IsValid)
            {
                allValid = false;
                errors.AddRange(result.Errors);
            }
        }

        if (allValid)
        {
            txtValidationStatus.Text = $"All busbars valid ({activeLayer.Busbars.Count} total)";
            txtValidationStatus.Foreground = Brushes.Green;
        }
        else
        {
            txtValidationStatus.Text = $"Validation errors:\n{string.Join("\n", errors.Take(3))}";
            txtValidationStatus.Foreground = Brushes.Red;
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
        txtInstructions.Visibility = Visibility.Collapsed;
        UpdateStatusBar("Drawing mode: Click to add points. Right-click or ESC to finish.");
    }

    private void FinishDrawing()
    {
        if (_currentPoints.Count < 2)
        {
            UpdateStatusBar("Need at least 2 points to create a busbar");
            CancelDrawing();
            return;
        }

        // Create busbar from points
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null) return;

        int busbarNumber = activeLayer.Busbars.Count + 1;
        var busbar = new Busbar($"Bar {busbarNumber}");

        // Create segments and bends
        for (int i = 0; i < _currentPoints.Count - 1; i++)
        {
            var start = _currentPoints[i];
            var end = _currentPoints[i + 1];
            double length = start.DistanceTo(end);

            var segment = new Segment(start, end, length);
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

        _isDrawing = false;
        _currentPoints.Clear();

        UpdateUI();
        UpdateStatusBar($"Busbar created: {busbar.Name}, Flat length: {busbar.FlatLength:F2}mm");
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
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }
        UpdateStatusBar("Drawing cancelled");
    }

    private double SnapAngle(double angle)
    {
        const double snapAngleDeg = 10.0;
        double angleDeg = angle * 180.0 / Math.PI;

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

        var point = e.GetPosition(drawingCanvas);
        var pt = new Point2D(point.X, point.Y);

        // If this is the second point, ask for exact length
        if (_currentPoints.Count >= 1)
        {
            var start = _currentPoints[_currentPoints.Count - 1];
            double currentLength = start.DistanceTo(pt);

            // Simple prompt for length
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Enter length in mm (current: {currentLength:F1}mm):",
                "Segment Length",
                currentLength.ToString("F0"));

            if (!string.IsNullOrEmpty(input) && double.TryParse(input, out double desiredLength) && desiredLength > 0)
            {
                // Adjust the end point to match desired length
                double dx = pt.X - start.X;
                double dy = pt.Y - start.Y;
                double angle = Math.Atan2(dy, dx);

                // Apply snapping to horizontal/vertical
                angle = SnapAngle(angle);

                // Calculate new end point at desired length
                pt = new Point2D(
                    start.X + desiredLength * Math.Cos(angle),
                    start.Y + desiredLength * Math.Sin(angle)
                );
            }
            else
            {
                // User cancelled
                UpdateStatusBar("Segment cancelled");
                return;
            }
        }

        _currentPoints.Add(pt);

        // Remove preview polygon
        if (_previewPolygon != null)
        {
            drawingCanvas.Children.Remove(_previewPolygon);
            _previewPolygon = null;
        }

        // Draw rectangle if we have at least 2 points
        if (_currentPoints.Count >= 2)
        {
            var lastIdx = _currentPoints.Count - 1;
            var start = _currentPoints[lastIdx - 1];
            var end = _currentPoints[lastIdx];

            DrawLine(start, end, Brushes.Blue, 1);
        }

        UpdateStatusBar($"Point {_currentPoints.Count} added at ({pt.X:F0}, {pt.Y:F0})");
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

        // Need at least one point to draw preview
        if (_currentPoints.Count == 0) return;

        var point = e.GetPosition(drawingCanvas);
        var lastPoint = _currentPoints[_currentPoints.Count - 1];

        // Draw preview rectangle
        const double busbarWidth = 10.0;
        const double minLength = 50.0;  // Minimum segment length
        double halfWidth = busbarWidth / 2.0;

        double dx = point.X - lastPoint.X;
        double dy = point.Y - lastPoint.Y;
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

        // Recalculate perpendicular based on normalized direction
        double perpX = -Math.Sin(angle);
        double perpY = Math.Cos(angle);

        // Calculate the 4 corner points
        Point2D p1 = new Point2D(lastPoint.X + perpX * halfWidth, lastPoint.Y + perpY * halfWidth);
        Point2D p2 = new Point2D(lastPoint.X - perpX * halfWidth, lastPoint.Y - perpY * halfWidth);
        Point2D p3 = new Point2D(endPoint.X - perpX * halfWidth, endPoint.Y - perpY * halfWidth);
        Point2D p4 = new Point2D(endPoint.X + perpX * halfWidth, endPoint.Y + perpY * halfWidth);

        _previewPolygon = new Polygon
        {
            Stroke = Brushes.Gray,
            Fill = Brushes.Transparent,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            IsHitTestVisible = false,  // Don't block mouse events!
            Points = new PointCollection
            {
                new System.Windows.Point(p1.X, p1.Y),
                new System.Windows.Point(p4.X, p4.Y),
                new System.Windows.Point(p3.X, p3.Y),
                new System.Windows.Point(p2.X, p2.Y)
            }
        };

        drawingCanvas.Children.Add(_previewPolygon);
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing)
        {
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

    private void DrawLine(Point2D start, Point2D end, System.Windows.Media.Brush color, double thickness)
    {
        // Draw a simple rectangle representing the busbar segment
        const double busbarWidth = 10.0;
        double halfWidth = busbarWidth / 2.0;

        // Calculate direction and perpendicular
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length == 0) return;

        double perpX = -dy / length;
        double perpY = dx / length;

        // Calculate the 4 corner points of the rectangle
        Point2D p1 = new Point2D(start.X + perpX * halfWidth, start.Y + perpY * halfWidth);
        Point2D p2 = new Point2D(start.X - perpX * halfWidth, start.Y - perpY * halfWidth);
        Point2D p3 = new Point2D(end.X - perpX * halfWidth, end.Y - perpY * halfWidth);
        Point2D p4 = new Point2D(end.X + perpX * halfWidth, end.Y + perpY * halfWidth);

        // Create a polygon for the rectangle
        var polygon = new Polygon
        {
            Stroke = color,
            Fill = Brushes.Transparent,
            StrokeThickness = 1,
            Points = new PointCollection
            {
                new System.Windows.Point(p1.X, p1.Y),
                new System.Windows.Point(p4.X, p4.Y),
                new System.Windows.Point(p3.X, p3.Y),
                new System.Windows.Point(p2.X, p2.Y)
            }
        };

        drawingCanvas.Children.Add(polygon);
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
        else if (e.Key == Key.Escape && _isDrawing)
        {
            CancelDrawing();
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
        // Future: Highlight selected busbar on canvas
    }
}