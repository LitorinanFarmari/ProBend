using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BusbarCAD.Models;
using BusbarCAD.Calculations;
using BusbarCAD.Export;
using BusbarCAD.Rendering;
using Microsoft.Win32;

namespace BusbarCAD;

/// <summary>
/// Segment information for display in the DataGrid during drawing
/// Properties match Segment model for consistent DataGrid binding
/// </summary>
public class SegmentInfo
{
    public double Angle { get; set; } = 0;
    public double Length { get; set; } = 0;
    public double BendAngle { get; set; } = 0;
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
    public Line? StartMarker { get; set; } = null; // Blue perpendicular line at start
    public Line? EndMarker { get; set; } = null; // Blue perpendicular line at end
}

/// <summary>
/// Interaction logic for MainWindow.x
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
    private Line? _currentStartMarker = null; // Current busbar's start marker (blue perpendicular line)
    private Line? _currentEndMarker = null; // Current busbar's end marker (blue perpendicular line)
    private Line? _snapReferenceLine = null; // Reference snap line at the start of first busbar
    private List<Ellipse> _snapReferencePoints = new List<Ellipse>(); // Snap points along the reference line
    private Line? _snapReferenceLineEnd = null; // Reference snap line at the end of first busbar
    private List<Ellipse> _snapReferencePointsEnd = new List<Ellipse>(); // Snap points along the end reference line
    private List<Line> _snapReferenceLinesCorners = new List<Line>(); // Reference snap lines at corners (bends)
    private List<Ellipse> _snapReferencePointsCorners = new List<Ellipse>(); // Snap points along corner reference lines

    // Dynamic reference line (shown when hovering over corner reference points)
    private Line? _dynamicSnapLine = null;
    private List<Ellipse> _dynamicSnapPoints = new List<Ellipse>();
    private Point2D? _dynamicSnapAnchor = null; // The locked diagonal point where dynamic line is anchored
    private double _dynamicSnapAngle = 0; // The angle of the dynamic line
    private double _dynamicSnapInterval = 10.0; // The spacing between snap points on the dynamic line
    private bool _dynamicSnapDisabled = false; // Prevent re-creating dynamic line after it's been disabled
    private Point2D? _dynamicSnapDisabledForPoint = null; // Track which snap point disabled the dynamic line

    // Snapping control
    private bool _isSnappingDisabled = false; // Universal snapping disable flag (for button and auto-disable after click)
    private int _lastVisibleReferenceLineIndex = -1; // Track which reference line is currently visible (-1=start, -2=end, 0+=corner index)
    private Busbar? _lastActiveBusbar = null; // Track the last active busbar for snap line reference
    private Busbar? _highlightedBusbar = null; // Track which busbar is currently highlighted

    // Rendering
    private BusbarRenderer? _busbarRenderer = null;

    // Move Points Mode
    private bool _isMovePointsMode = false;
    private List<(Busbar busbar, int pointIndex)> _selectedPoints = new List<(Busbar, int)>();
    private MoveDirection _currentMoveDirection = MoveDirection.None;
    private double _currentMoveDimension = 0;
    private List<Ellipse> _selectedPointMarkers = new List<Ellipse>();
    private Dictionary<(Busbar, int), Point2D> _originalPointPositions = new Dictionary<(Busbar, int), Point2D>();
    private bool _hasPreviewMove = false;
    private List<Ellipse> _allPointMarkers = new List<Ellipse>();
    private bool _isDraggingMoveControls = false;
    private System.Windows.Point _dragStartPoint;

    public enum MoveDirection
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    public MainWindow()
    {
        InitializeComponent();
        InitializeCanvasTransform();
        InitializeProject();
        this.PreviewKeyDown += Window_KeyDown;
        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize renderer after controls are loaded
        _busbarRenderer = new BusbarRenderer(drawingCanvas, _currentProject.MaterialSettings);
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

    private void MovePoints_Click(object sender, RoutedEventArgs e)
    {
        _isMovePointsMode = !_isMovePointsMode;

        // Update button appearance
        if (_isMovePointsMode)
        {
            btnMove.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            UpdateStatusBar("Move Points mode - Click points to select, use arrows to set direction, type dimension and press Enter");

            // Deactivate drawing mode
            _isDrawing = false;

            // Show move UI controls
            ShowMoveControls();
        }
        else
        {
            btnMove.Background = System.Windows.Media.Brushes.Transparent;
            UpdateStatusBar("Ready");

            // Clear selections and hide controls
            ClearPointSelection();
            HideMoveControls();
        }
    }

    private void ShowMoveControls()
    {
        moveControlsGrid.Visibility = Visibility.Visible;
        txtMoveDimension.Text = "0";
        _currentMoveDirection = MoveDirection.None;
        ResetDirectionButtonColors();

        // Show all busbar points
        ShowAllBusbarPoints();
    }

    private void HideMoveControls()
    {
        moveControlsGrid.Visibility = Visibility.Collapsed;
        txtMoveDimension.Text = "";
        _currentMoveDimension = 0;

        // Hide all busbar point markers
        HideAllBusbarPoints();
    }

    private void ShowAllBusbarPoints()
    {
        // Clear existing point markers
        HideAllBusbarPoints();

        var currentLayer = _currentProject.GetActiveLayer();
        if (currentLayer == null) return;

        // Draw small circles at all busbar points
        foreach (var busbar in currentLayer.Busbars)
        {
            for (int i = 0; i <= busbar.Segments.Count; i++)
            {
                var point = GetBusbarPoint(busbar, i);

                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Stroke = System.Windows.Media.Brushes.Gray,
                    StrokeThickness = 1,
                    Fill = System.Windows.Media.Brushes.White
                };

                Canvas.SetLeft(marker, point.X - 3);
                Canvas.SetTop(marker, point.Y - 3);
                drawingCanvas.Children.Add(marker);
                _allPointMarkers.Add(marker);
            }
        }
    }

    private void HideAllBusbarPoints()
    {
        foreach (var marker in _allPointMarkers)
        {
            drawingCanvas.Children.Remove(marker);
        }
        _allPointMarkers.Clear();
    }

    private void ClearPointSelection()
    {
        _selectedPoints.Clear();

        // Remove all visual markers
        foreach (var marker in _selectedPointMarkers)
        {
            drawingCanvas.Children.Remove(marker);
        }
        _selectedPointMarkers.Clear();
    }

    private void MoveDirection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string direction)
        {
            _currentMoveDirection = direction switch
            {
                "Up" => MoveDirection.Up,
                "Down" => MoveDirection.Down,
                "Left" => MoveDirection.Left,
                "Right" => MoveDirection.Right,
                _ => MoveDirection.None
            };

            ResetDirectionButtonColors();
            HighlightDirectionButton(btn);

            // Apply current dimension if any
            if (_currentMoveDimension != 0)
            {
                PreviewMove();
            }

            // Focus the dimension input
            txtMoveDimension.Focus();
            txtMoveDimension.SelectAll();
        }
    }

    private void ResetDirectionButtonColors()
    {
        btnMoveUp.Background = System.Windows.Media.Brushes.LightGray;
        btnMoveDown.Background = System.Windows.Media.Brushes.LightGray;
        btnMoveLeft.Background = System.Windows.Media.Brushes.LightGray;
        btnMoveRight.Background = System.Windows.Media.Brushes.LightGray;
    }

    private void HighlightDirectionButton(System.Windows.Controls.Button btn)
    {
        btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 150, 255));
    }

    private void MoveDimension_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyMove();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelMove();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _currentMoveDirection = MoveDirection.Up;
            ResetDirectionButtonColors();
            HighlightDirectionButton(btnMoveUp);
            PreviewMove();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _currentMoveDirection = MoveDirection.Down;
            ResetDirectionButtonColors();
            HighlightDirectionButton(btnMoveDown);
            PreviewMove();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            _currentMoveDirection = MoveDirection.Left;
            ResetDirectionButtonColors();
            HighlightDirectionButton(btnMoveLeft);
            PreviewMove();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _currentMoveDirection = MoveDirection.Right;
            ResetDirectionButtonColors();
            HighlightDirectionButton(btnMoveRight);
            PreviewMove();
            e.Handled = true;
        }
    }

    private void MoveDimension_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(txtMoveDimension.Text, out double dimension))
        {
            _currentMoveDimension = dimension;
            if (_currentMoveDirection != MoveDirection.None)
            {
                PreviewMove();
            }
        }
    }

    private void PreviewMove()
    {
        if (_selectedPoints.Count == 0 || _currentMoveDirection == MoveDirection.None)
            return;

        // Restore all previously moved points to original positions before applying new preview
        if (_hasPreviewMove)
        {
            foreach (var (key, originalPos) in _originalPointPositions.ToList())
            {
                SetBusbarPointDirect(key.Item1, key.Item2, originalPos);
            }
        }

        // Update original positions for all currently selected points
        // This handles the case where points were added or removed from selection
        foreach (var (busbar, pointIndex) in _selectedPoints)
        {
            if (!_originalPointPositions.ContainsKey((busbar, pointIndex)))
            {
                var point = GetBusbarPoint(busbar, pointIndex);
                _originalPointPositions[(busbar, pointIndex)] = new Point2D(point.X, point.Y);
            }
        }

        // Remove original positions for points that are no longer selected
        var pointsToRemove = _originalPointPositions.Keys
            .Where(key => !_selectedPoints.Contains(key))
            .ToList();
        foreach (var key in pointsToRemove)
        {
            _originalPointPositions.Remove(key);
        }

        _hasPreviewMove = true;

        // Calculate offset based on direction and dimension
        Point2D offset = _currentMoveDirection switch
        {
            MoveDirection.Up => new Point2D(0, -_currentMoveDimension),
            MoveDirection.Down => new Point2D(0, _currentMoveDimension),
            MoveDirection.Left => new Point2D(-_currentMoveDimension, 0),
            MoveDirection.Right => new Point2D(_currentMoveDimension, 0),
            _ => new Point2D(0, 0)
        };

        // Apply move from original positions (only the selected points, no propagation)
        foreach (var (busbar, pointIndex) in _selectedPoints)
        {
            var originalPoint = _originalPointPositions[(busbar, pointIndex)];
            var newPoint = new Point2D(originalPoint.X + offset.X, originalPoint.Y + offset.Y);
            SetBusbarPointDirect(busbar, pointIndex, newPoint);
        }

        // Redraw all busbars
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer != null)
        {
            _busbarRenderer?.RedrawAllBusbars(activeLayer);
        }

        // Update DataGrid if a busbar is selected
        if (lstBusbars.SelectedIndex >= 0 && lstBusbars.SelectedItem is Busbar selectedBusbar)
        {
            dgSegments.ItemsSource = null;
            dgSegments.ItemsSource = selectedBusbar.Segments;
        }
    }

    /// <summary>
    /// Sets a busbar point position directly without propagating to subsequent points.
    /// This is used by the Move Points tool to move only selected points independently.
    /// </summary>
    private void SetBusbarPointDirect(Busbar busbar, int pointIndex, Point2D newPosition)
    {
        if (busbar.Segments.Count == 0) return;
        if (pointIndex < 0 || pointIndex > busbar.Segments.Count) return;

        // Update the segment(s) that use this point
        if (pointIndex > 0)
        {
            // Update the EndPoint of the segment before this point
            busbar.Segments[pointIndex - 1].EndPoint = newPosition;
        }

        if (pointIndex < busbar.Segments.Count)
        {
            // Update the StartPoint of the segment at this point
            busbar.Segments[pointIndex].StartPoint = newPosition;
        }

        // Special case: if this is the last point, also update the last segment's endpoint
        if (pointIndex == busbar.Segments.Count && busbar.Segments.Count > 0)
        {
            busbar.Segments[busbar.Segments.Count - 1].EndPoint = newPosition;
        }
    }

    private void ApplyMove()
    {
        if (_selectedPoints.Count == 0 || _currentMoveDirection == MoveDirection.None)
            return;

        // Move is already applied by PreviewMove, just clear state
        int pointCount = _selectedPoints.Count;

        // Recalculate bend angles for all affected busbars
        var affectedBusbars = _selectedPoints.Select(p => p.busbar).Distinct().ToList();
        foreach (var busbar in affectedBusbars)
        {
            RecalculateBendAngles(busbar);
        }

        // Redraw all busbars
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer != null)
        {
            _busbarRenderer?.RedrawAllBusbars(activeLayer);
        }

        ClearPointSelection();
        txtMoveDimension.Text = "0";
        _currentMoveDimension = 0;
        _currentMoveDirection = MoveDirection.None;
        ResetDirectionButtonColors();
        _originalPointPositions.Clear();
        _hasPreviewMove = false;

        UpdateStatusBar($"Move applied to {pointCount} point(s)");

        // Exit move mode
        _isMovePointsMode = false;
        btnMove.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        HideMoveControls();

        // Force DataGrid refresh AFTER exiting move mode
        if (lstBusbars.SelectedIndex >= 0 && activeLayer != null && lstBusbars.SelectedIndex < activeLayer.Busbars.Count)
        {
            var selectedBusbar = activeLayer.Busbars[lstBusbars.SelectedIndex];
            dgSegments.ItemsSource = null;
            dgSegments.ItemsSource = new List<Segment>(selectedBusbar.Segments);
        }

        UpdateStatusBar("Ready");
    }

    /// <summary>
    /// Recalculates bend angles and updates both Bend objects and Segment.BendAngle based on current geometry
    /// </summary>
    private void RecalculateBendAngles(Busbar busbar)
    {
        // Update bend angles based on current segment directions
        for (int i = 0; i < busbar.Segments.Count - 1; i++)
        {
            var seg1 = busbar.Segments[i];
            var seg2 = busbar.Segments[i + 1];

            // Calculate angle between segments
            double angle1 = seg1.AngleRadians;
            double angle2 = seg2.AngleRadians;
            double bendAngle = (angle2 - angle1) * 180.0 / Math.PI;

            // Normalize to -180 to 180
            while (bendAngle > 180) bendAngle -= 360;
            while (bendAngle < -180) bendAngle += 360;

            // Update the bend object
            if (i < busbar.Bends.Count)
            {
                busbar.Bends[i].Angle = bendAngle;
            }

            // Update the segment's BendAngle (for the segment after the bend)
            if (i + 1 < busbar.Segments.Count)
            {
                busbar.Segments[i + 1].BendAngle = bendAngle;
            }
        }

        // First segment always has 0 bend angle
        if (busbar.Segments.Count > 0)
        {
            busbar.Segments[0].BendAngle = 0;
        }
    }

    private void CancelMove()
    {
        // Restore original positions if there was a preview
        if (_hasPreviewMove)
        {
            foreach (var (key, originalPos) in _originalPointPositions)
            {
                SetBusbarPointDirect(key.Item1, key.Item2, originalPos);
            }

            var activeLayer = _currentProject.GetActiveLayer();
            if (activeLayer != null)
            {
                _busbarRenderer?.RedrawAllBusbars(activeLayer);
            }

            // Update DataGrid if needed
            if (lstBusbars.SelectedIndex >= 0 && lstBusbars.SelectedItem is Busbar selectedBusbar)
            {
                dgSegments.ItemsSource = null;
                dgSegments.ItemsSource = selectedBusbar.Segments;
            }
        }

        // Don't clear point selection - keep points selected so user can try a different move
        txtMoveDimension.Text = "0";
        _currentMoveDimension = 0;
        _currentMoveDirection = MoveDirection.None;
        ResetDirectionButtonColors();
        _originalPointPositions.Clear();
        _hasPreviewMove = false;

        UpdateStatusBar("Move cancelled");
    }

    private Point2D GetBusbarPoint(Busbar busbar, int pointIndex)
    {
        if (pointIndex == 0)
            return busbar.Segments[0].StartPoint;
        else
            return busbar.Segments[pointIndex - 1].EndPoint;
    }

    private void MoveDragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingMoveControls = true;
            _dragStartPoint = e.GetPosition(drawingCanvas);
            moveDragHandle.CaptureMouse();
            e.Handled = true;
        }
    }

    private void MoveDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingMoveControls && e.LeftButton == MouseButtonState.Pressed)
        {
            var currentPos = e.GetPosition(drawingCanvas);
            var offset = currentPos - _dragStartPoint;

            double newLeft = Canvas.GetLeft(moveControlsGrid) + offset.X;
            double newTop = Canvas.GetTop(moveControlsGrid) + offset.Y;

            // Keep within canvas bounds
            newLeft = Math.Max(0, Math.Min(newLeft, drawingCanvas.ActualWidth - moveControlsGrid.ActualWidth));
            newTop = Math.Max(0, Math.Min(newTop, drawingCanvas.ActualHeight - moveControlsGrid.ActualHeight));

            Canvas.SetLeft(moveControlsGrid, newLeft);
            Canvas.SetTop(moveControlsGrid, newTop);

            _dragStartPoint = currentPos;
            e.Handled = true;
        }
    }

    private void MoveDragHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingMoveControls)
        {
            _isDraggingMoveControls = false;
            moveDragHandle.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void HandlePointSelection(MouseButtonEventArgs e)
    {
        var clickPos = GetCanvasMousePosition(e);
        const double selectionRadius = 10.0; // Distance threshold for selecting a point

        // Find the nearest point within selection radius
        Busbar? nearestBusbar = null;
        int nearestPointIndex = -1;
        double nearestDistance = selectionRadius;

        var currentLayer = _currentProject.GetActiveLayer();
        if (currentLayer == null) return;

        // Check all busbars and their points
        foreach (var busbar in currentLayer.Busbars)
        {
            for (int i = 0; i <= busbar.Segments.Count; i++)
            {
                var point = GetBusbarPoint(busbar, i);
                double distance = point.DistanceTo(clickPos);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestBusbar = busbar;
                    nearestPointIndex = i;
                }
            }
        }

        // If we found a point, toggle its selection
        if (nearestBusbar != null && nearestPointIndex >= 0)
        {
            var pointKey = (nearestBusbar, nearestPointIndex);
            int existingIndex = _selectedPoints.FindIndex(p => p.busbar == nearestBusbar && p.pointIndex == nearestPointIndex);

            if (existingIndex >= 0)
            {
                // Deselect: remove from list and remove visual marker
                _selectedPoints.RemoveAt(existingIndex);
                if (existingIndex < _selectedPointMarkers.Count)
                {
                    drawingCanvas.Children.Remove(_selectedPointMarkers[existingIndex]);
                    _selectedPointMarkers.RemoveAt(existingIndex);
                }
                UpdateStatusBar($"Point deselected. {_selectedPoints.Count} point(s) selected.");
            }
            else
            {
                // Select: add to list and add visual marker
                _selectedPoints.Add(pointKey);

                var point = GetBusbarPoint(nearestBusbar, nearestPointIndex);

                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Stroke = System.Windows.Media.Brushes.Blue,
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent
                };

                Canvas.SetLeft(marker, point.X - 4);
                Canvas.SetTop(marker, point.Y - 4);
                drawingCanvas.Children.Add(marker);
                _selectedPointMarkers.Add(marker);

                UpdateStatusBar($"Point selected. {_selectedPoints.Count} point(s) selected. Use arrows to set direction, type dimension and press Enter.");
            }
        }
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
        _isSnappingDisabled = false; // Re-enable snapping for new drawing
        _lastVisibleReferenceLineIndex = -1; // Reset visible reference line tracker
        _currentBusbarIndex = -1;
        _waitingForLengthInput = false;  // Reset input state
        _isEditingLength = false;
        _mouseStopTimer?.Stop();  // Stop any running timer
        UpdateSegmentList();
        txtBusbarName.Text = _nextBusbarLetter.ToString();

        // Clear the busbar selection when starting a new drawing
        lstBusbars.SelectedIndex = -1;

        txtInstructions.Visibility = Visibility.Collapsed;

        // Show snap reference line if there's an existing busbar
        ShowSnapReferenceLine();

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
            Angle = angle,
            Length = length,
            BendAngle = angle  // Bend angle is the same as the segment angle (relative to previous segment)
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
                Shapes = new List<Shape>(_currentShapes),
                StartMarker = _currentStartMarker,
                EndMarker = _currentEndMarker
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
            savedBusbar.StartMarker = _currentStartMarker;
            savedBusbar.EndMarker = _currentEndMarker;

            // Update the ListBox item
            lstBusbars.Items[_currentBusbarIndex] = busbarName;
        }

        // Clear current markers
        _currentStartMarker = null;
        _currentEndMarker = null;
    }

    private void HighlightBusbar(Busbar? busbar)
    {
        // Use the renderer's highlight method if available
        if (_busbarRenderer != null)
        {
            _busbarRenderer.HighlightBusbar(_highlightedBusbar, busbar);
        }
        _highlightedBusbar = busbar;
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

        int finishedBusbarIndex = -1;

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

                var segment = new Segment(start, end);

                // Set the WasForcedToMinimum flag if we have tracking data for this segment
                if (i < _segmentsForcedToMinimum.Count)
                {
                    segment.WasForcedToMinimum = _segmentsForcedToMinimum[i];
                }

                // Set bend angle: 0 for first segment, otherwise the angle of the previous bend
                if (i == 0)
                {
                    segment.BendAngle = 0;
                }
                else if (i > 0 && busbar.Bends.Count > 0)
                {
                    segment.BendAngle = busbar.Bends[i - 1].Angle;
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

            // Store the index of the newly added busbar
            finishedBusbarIndex = activeLayer.Busbars.Count - 1;

            // Set as last active busbar for snap line reference
            _lastActiveBusbar = busbar;

            // Clear temporary drawing shapes from canvas
            foreach (var shape in _currentShapes)
            {
                drawingCanvas.Children.Remove(shape);
            }

            // Draw the busbar using the renderer (stores visuals in busbar.VisualShapes)
            if (_busbarRenderer != null)
            {
                _busbarRenderer.DrawBusbar(busbar, true); // highlighted since it's the active one
            }
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

        // Hide snap reference line
        HideSnapReferenceLine();

        // Hide dynamic snap line
        HideDynamicSnapLine();

        // Update UI to refresh the busbar list
        UpdateUI();

        // Increment to next letter for next busbar (a -> b -> c ... z -> aa -> ab ...)
        if (_nextBusbarLetter == 'z')
        {
            _nextBusbarLetter = 'a'; // Could be extended to 'aa', 'ab', etc. if needed
        }
        else
        {
            _nextBusbarLetter++;
        }

        UpdateStatusBar($"Busbar '{busbarName}' finished");

        // Select the finished busbar in the ListBox after UI update completes
        // Use Dispatcher to ensure the list is fully updated before selection
        if (finishedBusbarIndex >= 0)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (finishedBusbarIndex < lstBusbars.Items.Count)
                {
                    lstBusbars.SelectedIndex = finishedBusbarIndex;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
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

        // Hide snap reference line
        HideSnapReferenceLine();

        // Hide dynamic snap line
        HideDynamicSnapLine();

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
        // Only apply this restriction when we have at least 2 points (i.e., one completed segment)
        if (_currentPoints.Count >= 2)
        {
            // Get the angle of the previous segment
            var prevStart = _currentPoints[_currentPoints.Count - 2];
            var prevEnd = _currentPoints[_currentPoints.Count - 1];
            double prevAngleRad = Math.Atan2(prevEnd.Y - prevStart.Y, prevEnd.X - prevStart.X);

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
        // Handle Move Points mode
        if (_isMovePointsMode)
        {
            HandlePointSelection(e);
            return;
        }

        if (!_isDrawing) return;

        // Don't interfere with panning
        if (_isPanning) return;

        // Commit any active DataGrid edits and clear focus to ensure single-click works
        // This prevents the first click from being consumed by DataGrid losing focus
        if (dgSegments.IsKeyboardFocusWithin)
        {
            dgSegments.CommitEdit();
            dgSegments.CommitEdit(); // Need to call twice: once for cell, once for row
            Keyboard.ClearFocus();
        }

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

        // Handle snapping with priority: dynamic line first, then regular reference lines
        bool isSnappedToReferenceLine = false;
        bool usedDynamicSnap = false;

        if (_currentPoints.Count >= 1)
        {
            var lastPoint = _currentPoints[_currentPoints.Count - 1];

            // First, try dynamic line snapping if we have an active dynamic line
            if (_dynamicSnapAnchor != null)
            {
                bool shouldBreakSnap;
                Point2D dynamicSnappedPos = SnapToDynamicLine(pt, lastPoint, out shouldBreakSnap);

                if (shouldBreakSnap)
                {
                    // Moving beyond dynamic line, break the snap and hide dynamic line
                    _dynamicSnapDisabledForPoint = _dynamicSnapAnchor; // Remember which point was disabled
                    HideDynamicSnapLine();
                    _dynamicSnapDisabled = true; // Prevent re-creating until snapping to different point
                }
                else
                {
                    // Successfully snapped to dynamic line
                    pt = dynamicSnappedPos;
                    isSnappedToReferenceLine = true;
                    usedDynamicSnap = true;
                }
            }
        }

        // If not using dynamic snap, check regular reference line snapping
        if (!usedDynamicSnap && (_snapReferenceLine != null || _snapReferenceLineEnd != null || _snapReferenceLinesCorners.Count > 0))
        {
            Point2D snappedPos = SnapToReferenceLine(pt);
            // Only snap if cursor is within snapping distance (10mm)
            double snapDistance = pt.DistanceTo(snappedPos);
            if (snapDistance <= 10.0)
            {
                pt = snappedPos;
                isSnappedToReferenceLine = true;
            }
        }

        // Apply angle snapping if we have at least one existing point AND not snapped to reference line
        if (_currentPoints.Count >= 1 && !isSnappedToReferenceLine)
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

        // Add the point
        _currentPoints.Add(pt);

        // Disable snapping after placing a point until the visible reference line changes
        _isSnappingDisabled = true;

        // Hide the dynamic snap line since we've placed a point
        HideDynamicSnapLine();
        _dynamicSnapDisabled = false; // Allow new dynamic line for next segment

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
        // Handle editing a selected busbar's segment length (not during drawing)
        if (!_isDrawing && !_waitingForLengthInput && e.EditAction == DataGridEditAction.Commit)
        {
            HandleBusbarSegmentEdit(e);
            return;
        }

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

        // Update which reference line is visible based on cursor proximity
        UpdateReferenceLineVisibility(currentPt);

        // Handle snapping with priority: dynamic line first, then regular reference lines
        Point2D snappedCursor = currentPt;

        // Need at least one point to use dynamic line snapping
        if (_currentPoints.Count == 0)
        {
            // No points yet, but still check for reference line snapping (if not disabled)
            if (!_isSnappingDisabled && (_snapReferenceLine != null || _snapReferenceLineEnd != null))
            {
                Point2D refSnappedPos = SnapToReferenceLine(currentPt);
                double refSnapDistance = currentPt.DistanceTo(refSnappedPos);
                if (refSnapDistance <= 10.0)
                {
                    snappedCursor = refSnappedPos;
                }
            }

            // Show preview at cursor location (or snapped location)
            _previewPoint = new Ellipse
            {
                Width = 2,
                Height = 2,
                Fill = Brushes.Blue,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                IsHitTestVisible = false  // Don't block mouse events
            };
            Canvas.SetLeft(_previewPoint, snappedCursor.X - 1);
            Canvas.SetTop(_previewPoint, snappedCursor.Y - 1);
            drawingCanvas.Children.Add(_previewPoint);
            return;
        }

        var lastPoint = _currentPoints[_currentPoints.Count - 1];

        // Calculate preview measurements
        const double minLength = 50.0;  // Minimum segment length

        // First, try dynamic line snapping if we have an active dynamic line
        bool isSnappedToReferenceLine = false;
        bool usedDynamicSnap = false;

        // Only process snapping if not disabled
        if (!_isSnappingDisabled)
        {
            if (_dynamicSnapAnchor != null)
            {
                // We have an active dynamic line, try to snap to it
                // The angle stays locked from when it was created
                bool shouldBreakSnap;
                Point2D dynamicSnappedPos = SnapToDynamicLine(currentPt, lastPoint, out shouldBreakSnap);

                if (shouldBreakSnap)
                {
                    // Moving too far perpendicular or beyond 5 points, break the snap and hide dynamic line
                    _dynamicSnapDisabledForPoint = _dynamicSnapAnchor; // Remember which point was disabled
                    HideDynamicSnapLine();
                    _dynamicSnapDisabled = true; // Prevent re-creating until snapping to different point
                }
                else
                {
                    // Successfully snapped to dynamic line
                    snappedCursor = dynamicSnappedPos;
                    isSnappedToReferenceLine = true; // Treat as reference line snap for angle handling
                    usedDynamicSnap = true;
                }
            }

            // If not using dynamic snap, check regular reference line snapping
            if (!usedDynamicSnap && (_snapReferenceLine != null || _snapReferenceLineEnd != null || _snapReferenceLinesCorners.Count > 0))
            {
                Point2D refSnappedPos = SnapToReferenceLine(currentPt);
                double refSnapDistance = currentPt.DistanceTo(refSnappedPos);
                if (refSnapDistance <= 10.0)
                {
                    snappedCursor = refSnappedPos;
                    isSnappedToReferenceLine = true;

                    // Check if we're snapping to a corner diagonal reference line
                    // If so, we should create a dynamic line
                    // Reset disabled flag if we're snapping to a different point than the one that was disabled
                    if (_dynamicSnapDisabled && _dynamicSnapDisabledForPoint != null)
                    {
                        double distToDisabledPoint = refSnappedPos.DistanceTo(_dynamicSnapDisabledForPoint.Value);
                        // Diagonal snap points are spaced ~14mm apart, so use 5mm threshold to detect different point
                        if (distToDisabledPoint > 5.0)
                        {
                            _dynamicSnapDisabled = false;
                            _dynamicSnapDisabledForPoint = null;
                        }
                    }

                    if (_snapReferenceLinesCorners.Count > 0 && _dynamicSnapAnchor == null && !_dynamicSnapDisabled)
                    {
                        // First check if we're actually snapping to a diagonal line (not start/end line)
                        // by checking if snapped point is close to any diagonal snap point
                        bool isOnDiagonalLine = false;
                        foreach (var cornerSnapPoint in _snapReferencePointsCorners)
                        {
                            double px = Canvas.GetLeft(cornerSnapPoint) + 1.5;
                            double py = Canvas.GetTop(cornerSnapPoint) + 1.5;
                            double dist = Math.Sqrt(Math.Pow(snappedCursor.X - px, 2) + Math.Pow(snappedCursor.Y - py, 2));
                            if (dist < 2.0)
                            {
                                isOnDiagonalLine = true;
                                break;
                            }
                        }

                        if (isOnDiagonalLine)
                        {
                        var activeLayer = _currentProject.GetActiveLayer();
                        if (activeLayer != null && activeLayer.Busbars.Count > 0)
                        {
                            var targetBusbar = _lastActiveBusbar ?? activeLayer.Busbars[0];
                            if (targetBusbar.Segments.Count >= 2)
                            {
                                // Find which diagonal line we snapped to by finding the closest corner
                                double closestCornerDist = double.MaxValue;
                                int closestCornerIndex = -1;

                                for (int i = 0; i < targetBusbar.Segments.Count - 1; i++)
                                {
                                    Point2D cornerPoint = targetBusbar.Segments[i].EndPoint;
                                    double distToCorner = snappedCursor.DistanceTo(cornerPoint);

                                    if (distToCorner < closestCornerDist)
                                    {
                                        closestCornerDist = distToCorner;
                                        closestCornerIndex = i;
                                    }
                                }

                                // Create dynamic line for the closest corner's diagonal
                                if (closestCornerIndex >= 0)
                                {
                                    // Calculate the angle from last clicked point to the snapped cursor position
                                    // This is the direction of the dynamic line
                                    double dxDynamic = snappedCursor.X - lastPoint.X;
                                    double dyDynamic = snappedCursor.Y - lastPoint.Y;
                                    double dynamicAngle = Math.Atan2(dyDynamic, dxDynamic);

                                    // Get the corner angle difference for spacing calculation
                                    int i = closestCornerIndex;
                                    var currentSegment = targetBusbar.Segments[i];
                                    var nextSegment = targetBusbar.Segments[i + 1];

                                    // Use stored angles from segments (convert from degrees to radians)
                                    double angle1 = currentSegment.Angle * Math.PI / 180.0;
                                    double angle2 = nextSegment.Angle * Math.PI / 180.0;

                                    double angleDiff = angle2 - angle1;
                                    while (angleDiff > Math.PI) angleDiff -= 2 * Math.PI;
                                    while (angleDiff < -Math.PI) angleDiff += 2 * Math.PI;

                                    // Calculate angle between dynamic line and both segments
                                    // Use the one that gives the angle closest to 90° (most perpendicular)
                                    double angleBetween1 = dynamicAngle - angle1;
                                    while (angleBetween1 > Math.PI) angleBetween1 -= 2 * Math.PI;
                                    while (angleBetween1 < -Math.PI) angleBetween1 += 2 * Math.PI;

                                    double angleBetween2 = dynamicAngle - angle2;
                                    while (angleBetween2 > Math.PI) angleBetween2 -= 2 * Math.PI;
                                    while (angleBetween2 < -Math.PI) angleBetween2 += 2 * Math.PI;

                                    // Normalize both to acute angles (0 to 90 degrees)
                                    angleBetween1 = Math.Abs(angleBetween1);
                                    if (angleBetween1 > Math.PI / 2) angleBetween1 = Math.PI - angleBetween1;

                                    angleBetween2 = Math.Abs(angleBetween2);
                                    if (angleBetween2 > Math.PI / 2) angleBetween2 = Math.PI - angleBetween2;

                                    // Use the angle that is closer to 90° (perpendicular)
                                    // This is the angle with value closer to π/2
                                    double diff1From90 = Math.Abs(angleBetween1 - Math.PI / 2);
                                    double diff2From90 = Math.Abs(angleBetween2 - Math.PI / 2);

                                    double angleBetween = (diff1From90 < diff2From90) ? angleBetween1 : angleBetween2;

                                    // Create dynamic line at the snapped position
                                    ShowDynamicSnapLine(snappedCursor, dynamicAngle, angleBetween);
                                }
                            }
                        }
                        }
                    }
                }
            }
        }

        double dx = snappedCursor.X - lastPoint.X;
        double dy = snappedCursor.Y - lastPoint.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.1) return; // Too short to draw

        // Enforce minimum length of 50mm
        if (length < minLength)
        {
            length = minLength;
        }

        // Normalize direction and apply length
        double angle = Math.Atan2(dy, dx);

        // Only apply horizontal/vertical angle snapping if NOT snapped to reference line
        if (!isSnappedToReferenceLine)
        {
            // Snap to horizontal or vertical if within 10 degrees
            angle = SnapAngle(angle);
        }

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

    private void UpdateLivePreviewMeasurements(Point2D start, Point2D end, double? lengthOverride = null)
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
            Angle = previewAngle,
            Length = lengthOverride ?? previewLength,
            BendAngle = previewAngle
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
            _currentStartMarker = DrawPerpendicularMarker(firstPoint, startAngle, false, true);
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
            _currentEndMarker = DrawPerpendicularMarker(lastPoint, endAngle, false, true);
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

    private Line DrawPerpendicularMarker(Point2D position, double directionAngle, bool isPreview, bool isEndPoint = false)
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

        return line;
    }

    private void ShowSnapReferenceLine()
    {
        // Remove any existing snap reference line
        HideSnapReferenceLine();

        // Only show if we have a last active busbar
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0) return;

        // Get the last active busbar (or fall back to first busbar)
        var targetBusbar = _lastActiveBusbar ?? activeLayer.Busbars[0];
        if (targetBusbar.Segments.Count == 0) return;

        // Get the start point and direction of the first segment
        var firstSegment = targetBusbar.Segments[0];
        Point2D startPoint = firstSegment.StartPoint;

        // Calculate direction angle from first segment
        double dx = firstSegment.EndPoint.X - firstSegment.StartPoint.X;
        double dy = firstSegment.EndPoint.Y - firstSegment.StartPoint.Y;
        double directionAngle = Math.Atan2(dy, dx);

        // Perpendicular angle (90 degrees offset)
        double perpAngle = directionAngle + Math.PI / 2.0;

        // Extension: 50mm on each side
        const double extension = 50.0;

        // Calculate the two endpoints of the reference line
        double cos = Math.Cos(perpAngle);
        double sin = Math.Sin(perpAngle);

        Point2D lineStart = new Point2D(
            startPoint.X - extension * cos,
            startPoint.Y - extension * sin
        );

        Point2D lineEnd = new Point2D(
            startPoint.X + extension * cos,
            startPoint.Y + extension * sin
        );

        // Create the reference line (light beige color)
        _snapReferenceLine = new Line
        {
            X1 = lineStart.X,
            Y1 = lineStart.Y,
            X2 = lineEnd.X,
            Y2 = lineEnd.Y,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 220)), // Light beige
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(_snapReferenceLine);

        // Create snap points every 10mm along the line
        const double snapInterval = 10.0; // Busbar thickness
        int numSnapPoints = (int)(extension * 2 / snapInterval) + 1;

        for (int i = 0; i < numSnapPoints; i++)
        {
            double t = i * snapInterval - extension;
            Point2D snapPoint = new Point2D(
                startPoint.X + t * cos,
                startPoint.Y + t * sin
            );

            var snapDot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 180)), // Slightly darker beige
                IsHitTestVisible = false
            };
            Canvas.SetLeft(snapDot, snapPoint.X - 1.5);
            Canvas.SetTop(snapDot, snapPoint.Y - 1.5);
            drawingCanvas.Children.Add(snapDot);
            _snapReferencePoints.Add(snapDot);
        }

        // Now create the end reference line
        var lastSegment = targetBusbar.Segments[targetBusbar.Segments.Count - 1];
        Point2D endPoint = lastSegment.EndPoint;

        // Calculate direction angle from last segment
        double dxEnd = lastSegment.EndPoint.X - lastSegment.StartPoint.X;
        double dyEnd = lastSegment.EndPoint.Y - lastSegment.StartPoint.Y;
        double directionAngleEnd = Math.Atan2(dyEnd, dxEnd);

        // Perpendicular angle (90 degrees offset)
        double perpAngleEnd = directionAngleEnd + Math.PI / 2.0;

        // Calculate the two endpoints of the end reference line
        double cosEnd = Math.Cos(perpAngleEnd);
        double sinEnd = Math.Sin(perpAngleEnd);

        Point2D lineStartEnd = new Point2D(
            endPoint.X - extension * cosEnd,
            endPoint.Y - extension * sinEnd
        );

        Point2D lineEndEnd = new Point2D(
            endPoint.X + extension * cosEnd,
            endPoint.Y + extension * sinEnd
        );

        // Create the end reference line (light beige color)
        _snapReferenceLineEnd = new Line
        {
            X1 = lineStartEnd.X,
            Y1 = lineStartEnd.Y,
            X2 = lineEndEnd.X,
            Y2 = lineEndEnd.Y,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 220)), // Light beige
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(_snapReferenceLineEnd);

        // Create snap points every 10mm along the end reference line
        for (int i = 0; i < numSnapPoints; i++)
        {
            double t = i * snapInterval - extension;
            Point2D snapPoint = new Point2D(
                endPoint.X + t * cosEnd,
                endPoint.Y + t * sinEnd
            );

            var snapDot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 180)), // Slightly darker beige
                IsHitTestVisible = false
            };
            Canvas.SetLeft(snapDot, snapPoint.X - 1.5);
            Canvas.SetTop(snapDot, snapPoint.Y - 1.5);
            drawingCanvas.Children.Add(snapDot);
            _snapReferencePointsEnd.Add(snapDot);
        }

        // Create corner (bend) reference lines at angle bisectors
        // Loop through all segments to find corners (where there are at least 3 segments, corners are at indices 1 to n-2)
        if (targetBusbar.Segments.Count >= 2)
        {
            for (int i = 0; i < targetBusbar.Segments.Count - 1; i++)
            {
                var currentSegment = targetBusbar.Segments[i];
                var nextSegment = targetBusbar.Segments[i + 1];

                // Get the corner point (end of current segment = start of next segment)
                Point2D cornerPoint = currentSegment.EndPoint;

                // Calculate angles of the two segments
                double dx1 = currentSegment.EndPoint.X - currentSegment.StartPoint.X;
                double dy1 = currentSegment.EndPoint.Y - currentSegment.StartPoint.Y;
                double angle1 = Math.Atan2(dy1, dx1);

                double dx2 = nextSegment.EndPoint.X - nextSegment.StartPoint.X;
                double dy2 = nextSegment.EndPoint.Y - nextSegment.StartPoint.Y;
                double angle2 = Math.Atan2(dy2, dx2);

                // Calculate the bisector angle (average of the two angles)
                // Need to handle angle wrapping properly
                double angleDiff = angle2 - angle1;
                // Normalize to -π to π
                while (angleDiff > Math.PI) angleDiff -= 2 * Math.PI;
                while (angleDiff < -Math.PI) angleDiff += 2 * Math.PI;

                double bisectorAngle = angle1 + angleDiff / 2.0;

                // The reference line should be perpendicular to the bisector (add 90 degrees)
                double perpBisectorAngle = bisectorAngle + Math.PI / 2.0;

                // Calculate the correct diagonal snap interval based on the corner angle
                // For a perpendicular offset of busbarThickness, the spacing along the bisector is:
                // spacing = busbarThickness / cos(angleDiff/2)
                // As angle gets smaller (nearly straight), spacing approaches busbar thickness
                // As angle gets larger (sharp turn), spacing increases
                const double busbarThickness = 10.0;
                double halfAngleDiff = Math.Abs(angleDiff) / 2.0;

                // Avoid division by zero for angles close to 180 degrees
                double cosHalfAngle = Math.Cos(halfAngleDiff);
                if (Math.Abs(cosHalfAngle) < 0.01) cosHalfAngle = 0.01;

                double diagonalSnapInterval = busbarThickness / cosHalfAngle;

                // Fixed number of snap points: ±5 points (11 total including center)
                const int numPointsPerSide = 5;

                // Calculate line extension based on snap points (5 points on each side)
                double cornerExtension = numPointsPerSide * diagonalSnapInterval;

                // Calculate the two endpoints of the corner reference line
                double cosBisector = Math.Cos(perpBisectorAngle);
                double sinBisector = Math.Sin(perpBisectorAngle);

                Point2D cornerLineStart = new Point2D(
                    cornerPoint.X - cornerExtension * cosBisector,
                    cornerPoint.Y - cornerExtension * sinBisector
                );

                Point2D cornerLineEnd = new Point2D(
                    cornerPoint.X + cornerExtension * cosBisector,
                    cornerPoint.Y + cornerExtension * sinBisector
                );

                // Create the corner reference line (light blue color to distinguish from end lines)
                var cornerLine = new Line
                {
                    X1 = cornerLineStart.X,
                    Y1 = cornerLineStart.Y,
                    X2 = cornerLineEnd.X,
                    Y2 = cornerLineEnd.Y,
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 235, 245)), // Light blue
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                drawingCanvas.Children.Add(cornerLine);
                _snapReferenceLinesCorners.Add(cornerLine);

                // Create points from negative side to positive side, including center point at 0
                for (int j = -numPointsPerSide; j <= numPointsPerSide; j++)
                {
                    double t = j * diagonalSnapInterval;
                    Point2D snapPoint = new Point2D(
                        cornerPoint.X + t * cosBisector,
                        cornerPoint.Y + t * sinBisector
                    );

                    var snapDot = new Ellipse
                    {
                        Width = 3,
                        Height = 3,
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(snapDot, snapPoint.X - 1.5);
                    Canvas.SetTop(snapDot, snapPoint.Y - 1.5);
                    drawingCanvas.Children.Add(snapDot);
                    _snapReferencePointsCorners.Add(snapDot);
                }
            }
        }
    }

    private void HideSnapReferenceLine()
    {
        if (_snapReferenceLine != null)
        {
            drawingCanvas.Children.Remove(_snapReferenceLine);
            _snapReferenceLine = null;
        }

        foreach (var point in _snapReferencePoints)
        {
            drawingCanvas.Children.Remove(point);
        }
        _snapReferencePoints.Clear();

        if (_snapReferenceLineEnd != null)
        {
            drawingCanvas.Children.Remove(_snapReferenceLineEnd);
            _snapReferenceLineEnd = null;
        }

        foreach (var point in _snapReferencePointsEnd)
        {
            drawingCanvas.Children.Remove(point);
        }
        _snapReferencePointsEnd.Clear();

        foreach (var line in _snapReferenceLinesCorners)
        {
            drawingCanvas.Children.Remove(line);
        }
        _snapReferenceLinesCorners.Clear();

        foreach (var point in _snapReferencePointsCorners)
        {
            drawingCanvas.Children.Remove(point);
        }
        _snapReferencePointsCorners.Clear();
    }

    private void ShowDynamicSnapLine(Point2D anchorPoint, double angleRadians, double angleBetweenDynamicAndSegment)
    {
        // Remove existing line if any
        HideDynamicSnapLine();

        // Store the anchor point and angle for locked snapping
        _dynamicSnapAnchor = anchorPoint;
        _dynamicSnapAngle = angleRadians;

        // Calculate direction vector
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);

        // Calculate dynamic snap interval based on angle between dynamic line and target segment
        // Each step along the dynamic line should create exactly one busbar thickness perpendicular offset
        // spacing = busbarThickness / sin(angle_between)
        const double busbarThickness = 10.0;

        // Avoid division by zero for very small or very large angles
        double sinAngleBetween = Math.Sin(Math.Abs(angleBetweenDynamicAndSegment));
        if (Math.Abs(sinAngleBetween) < 0.01) sinAngleBetween = 0.01;

        double dynamicSnapInterval = busbarThickness / sinAngleBetween;

        // Store the interval for use in SnapToDynamicLine
        _dynamicSnapInterval = dynamicSnapInterval;

        // Fixed number of snap points: ±5 points (11 total including center)
        const int numPointsPerSide = 5;

        // Calculate line extension based on snap points
        double extension = numPointsPerSide * dynamicSnapInterval;

        Point2D lineStart = new Point2D(
            anchorPoint.X - extension * cos,
            anchorPoint.Y - extension * sin
        );
        Point2D lineEnd = new Point2D(
            anchorPoint.X + extension * cos,
            anchorPoint.Y + extension * sin
        );

        // Create the yellow dynamic reference line
        _dynamicSnapLine = new Line
        {
            X1 = lineStart.X,
            Y1 = lineStart.Y,
            X2 = lineEnd.X,
            Y2 = lineEnd.Y,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 150)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        drawingCanvas.Children.Add(_dynamicSnapLine);

        // Create snap points at calculated intervals (±5 points = 11 total)
        for (int i = -numPointsPerSide; i <= numPointsPerSide; i++)
        {
            double distance = i * dynamicSnapInterval;
            Point2D snapDot = new Point2D(
                anchorPoint.X + distance * cos,
                anchorPoint.Y + distance * sin
            );

            var dot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 100)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, snapDot.X - 1.5);
            Canvas.SetTop(dot, snapDot.Y - 1.5);
            drawingCanvas.Children.Add(dot);
            _dynamicSnapPoints.Add(dot);
        }
    }

    private void HideDynamicSnapLine()
    {
        if (_dynamicSnapLine != null)
        {
            drawingCanvas.Children.Remove(_dynamicSnapLine);
            _dynamicSnapLine = null;
        }

        foreach (var point in _dynamicSnapPoints)
        {
            drawingCanvas.Children.Remove(point);
        }
        _dynamicSnapPoints.Clear();

        // Clear the locked anchor point, angle, and interval
        _dynamicSnapAnchor = null;
        _dynamicSnapAngle = 0;
        _dynamicSnapInterval = 10.0;
    }

    private void UpdateReferenceLineVisibility(Point2D cursorPos)
    {
        // If no reference lines exist, nothing to do
        if (_snapReferenceLine == null && _snapReferenceLineEnd == null && _snapReferenceLinesCorners.Count == 0)
            return;

        // Get the first busbar to calculate distances
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0) return;

        var targetBusbar = _lastActiveBusbar ?? activeLayer.Busbars[0];
        if (targetBusbar.Segments.Count == 0) return;

        // Find which reference line is closest to the cursor
        double minDistance = double.MaxValue;
        int closestType = -1; // 0 = start, 1 = end, 2+ = corner index

        // Check distance to start reference line
        if (_snapReferenceLine != null)
        {
            var firstSegment = targetBusbar.Segments[0];
            Point2D startPoint = firstSegment.StartPoint;
            double dist = cursorPos.DistanceTo(startPoint);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestType = 0;
            }
        }

        // Check distance to end reference line
        if (_snapReferenceLineEnd != null)
        {
            var lastSegment = targetBusbar.Segments[targetBusbar.Segments.Count - 1];
            Point2D endPoint = lastSegment.EndPoint;
            double dist = cursorPos.DistanceTo(endPoint);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestType = 1;
            }
        }

        // Check distance to corner reference lines
        for (int i = 0; i < targetBusbar.Segments.Count - 1; i++)
        {
            var currentSegment = targetBusbar.Segments[i];
            Point2D cornerPoint = currentSegment.EndPoint;
            double dist = cursorPos.DistanceTo(cornerPoint);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestType = 2 + i;
            }
        }

        // Check if the visible reference line has changed
        // If it has, re-enable snapping
        if (_lastVisibleReferenceLineIndex != closestType)
        {
            _isSnappingDisabled = false;
            _lastVisibleReferenceLineIndex = closestType;
        }

        // Update visibility based on closest type
        // Start line
        if (_snapReferenceLine != null)
        {
            _snapReferenceLine.Visibility = (closestType == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var point in _snapReferencePoints)
        {
            point.Visibility = (closestType == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        // End line
        if (_snapReferenceLineEnd != null)
        {
            _snapReferenceLineEnd.Visibility = (closestType == 1) ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var point in _snapReferencePointsEnd)
        {
            point.Visibility = (closestType == 1) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Corner lines
        for (int i = 0; i < _snapReferenceLinesCorners.Count; i++)
        {
            bool isVisible = (closestType == 2 + i);
            _snapReferenceLinesCorners[i].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        // Corner points - need to figure out which points belong to which corner
        // Since we add all corner points sequentially, we need to track point counts per corner
        var activeLayer2 = _currentProject.GetActiveLayer();
        if (activeLayer2 != null && activeLayer2.Busbars.Count > 0)
        {
            var fb = _lastActiveBusbar ?? activeLayer2.Busbars[0];
            int pointIndex = 0;
            for (int i = 0; i < fb.Segments.Count - 1; i++)
            {
                bool isVisible = (closestType == 2 + i);
                // Fixed number of points: ±5 (11 total including center)
                const int numPointsPerSide = 5;
                int totalPoints = numPointsPerSide * 2 + 1;

                for (int j = 0; j < totalPoints && pointIndex < _snapReferencePointsCorners.Count; j++)
                {
                    _snapReferencePointsCorners[pointIndex].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                    pointIndex++;
                }
            }
        }
    }

    private Point2D SnapToReferenceLine(Point2D clickPoint)
    {
        if (_snapReferenceLine == null && _snapReferenceLineEnd == null && _snapReferenceLinesCorners.Count == 0) return clickPoint;

        // Get the first busbar to calculate reference line direction
        var activeLayer = _currentProject.GetActiveLayer();
        if (activeLayer == null || activeLayer.Busbars.Count == 0) return clickPoint;

        var targetBusbar = _lastActiveBusbar ?? activeLayer.Busbars[0];
        if (targetBusbar.Segments.Count == 0) return clickPoint;

        Point2D? closestSnapPoint = null;
        double closestDistance = double.MaxValue;

        // Check start reference line
        if (_snapReferenceLine != null)
        {
            var firstSegment = targetBusbar.Segments[0];
            Point2D startPoint = firstSegment.StartPoint;

            // Calculate direction angle from first segment
            double dx = firstSegment.EndPoint.X - firstSegment.StartPoint.X;
            double dy = firstSegment.EndPoint.Y - firstSegment.StartPoint.Y;
            double directionAngle = Math.Atan2(dy, dx);

            // Perpendicular angle (90 degrees offset)
            double perpAngle = directionAngle + Math.PI / 2.0;

            // Calculate perpendicular distance from click point to reference line
            double cos = Math.Cos(perpAngle);
            double sin = Math.Sin(perpAngle);

            // Vector from startPoint to clickPoint
            double vx = clickPoint.X - startPoint.X;
            double vy = clickPoint.Y - startPoint.Y;

            // Project onto perpendicular direction (distance along the reference line)
            double distanceAlongLine = vx * cos + vy * sin;

            // Snap to nearest 10mm interval
            const double snapInterval = 10.0;
            double snappedDistance = Math.Round(distanceAlongLine / snapInterval) * snapInterval;

            // Clamp to ±50mm
            const double maxDistance = 50.0;
            if (snappedDistance >= -maxDistance && snappedDistance <= maxDistance)
            {
                // Calculate snapped point on the start reference line
                Point2D snappedPoint = new Point2D(
                    startPoint.X + snappedDistance * cos,
                    startPoint.Y + snappedDistance * sin
                );

                double dist = clickPoint.DistanceTo(snappedPoint);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestSnapPoint = snappedPoint;
                }
            }
        }

        // Check end reference line
        if (_snapReferenceLineEnd != null)
        {
            var lastSegment = targetBusbar.Segments[targetBusbar.Segments.Count - 1];
            Point2D endPoint = lastSegment.EndPoint;

            // Calculate direction angle from last segment
            double dx = lastSegment.EndPoint.X - lastSegment.StartPoint.X;
            double dy = lastSegment.EndPoint.Y - lastSegment.StartPoint.Y;
            double directionAngle = Math.Atan2(dy, dx);

            // Perpendicular angle (90 degrees offset)
            double perpAngle = directionAngle + Math.PI / 2.0;

            // Calculate perpendicular distance from click point to reference line
            double cos = Math.Cos(perpAngle);
            double sin = Math.Sin(perpAngle);

            // Vector from endPoint to clickPoint
            double vx = clickPoint.X - endPoint.X;
            double vy = clickPoint.Y - endPoint.Y;

            // Project onto perpendicular direction (distance along the reference line)
            double distanceAlongLine = vx * cos + vy * sin;

            // Snap to nearest 10mm interval
            const double snapInterval = 10.0;
            double snappedDistance = Math.Round(distanceAlongLine / snapInterval) * snapInterval;

            // Clamp to ±50mm
            const double maxDistance = 50.0;
            if (snappedDistance >= -maxDistance && snappedDistance <= maxDistance)
            {
                // Calculate snapped point on the end reference line
                Point2D snappedPoint = new Point2D(
                    endPoint.X + snappedDistance * cos,
                    endPoint.Y + snappedDistance * sin
                );

                double dist = clickPoint.DistanceTo(snappedPoint);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestSnapPoint = snappedPoint;
                }
            }
        }

        // Check corner reference lines
        if (_snapReferenceLinesCorners.Count > 0 && targetBusbar.Segments.Count >= 2)
        {
            for (int i = 0; i < targetBusbar.Segments.Count - 1; i++)
            {
                var currentSegment = targetBusbar.Segments[i];
                var nextSegment = targetBusbar.Segments[i + 1];

                // Get the corner point
                Point2D cornerPoint = currentSegment.EndPoint;

                // Calculate angles of the two segments
                double dx1 = currentSegment.EndPoint.X - currentSegment.StartPoint.X;
                double dy1 = currentSegment.EndPoint.Y - currentSegment.StartPoint.Y;
                double angle1 = Math.Atan2(dy1, dx1);

                double dx2 = nextSegment.EndPoint.X - nextSegment.StartPoint.X;
                double dy2 = nextSegment.EndPoint.Y - nextSegment.StartPoint.Y;
                double angle2 = Math.Atan2(dy2, dx2);

                // Calculate the bisector angle
                double angleDiff = angle2 - angle1;
                while (angleDiff > Math.PI) angleDiff -= 2 * Math.PI;
                while (angleDiff < -Math.PI) angleDiff += 2 * Math.PI;

                double bisectorAngle = angle1 + angleDiff / 2.0;

                // The reference line should be perpendicular to the bisector (add 90 degrees)
                double perpBisectorAngle = bisectorAngle + Math.PI / 2.0;

                double cosBisector = Math.Cos(perpBisectorAngle);
                double sinBisector = Math.Sin(perpBisectorAngle);

                // Vector from cornerPoint to clickPoint
                double vx = clickPoint.X - cornerPoint.X;
                double vy = clickPoint.Y - cornerPoint.Y;

                // Project onto bisector direction
                double distanceAlongLine = vx * cosBisector + vy * sinBisector;

                // Calculate the correct diagonal snap interval based on the corner angle
                const double busbarThickness = 10.0;
                double halfAngleDiff = Math.Abs(angleDiff) / 2.0;
                double cosHalfAngle = Math.Cos(halfAngleDiff);
                if (Math.Abs(cosHalfAngle) < 0.01) cosHalfAngle = 0.01;
                double diagonalSnapInterval = busbarThickness / cosHalfAngle;

                double snappedDistance = Math.Round(distanceAlongLine / diagonalSnapInterval) * diagonalSnapInterval;

                // Clamp to ±5 points
                const int maxPoints = 5;
                double maxDistance = maxPoints * diagonalSnapInterval;
                if (snappedDistance >= -maxDistance && snappedDistance <= maxDistance)
                {
                    // Calculate snapped point on the corner reference line
                    Point2D snappedPoint = new Point2D(
                        cornerPoint.X + snappedDistance * cosBisector,
                        cornerPoint.Y + snappedDistance * sinBisector
                    );

                    double dist = clickPoint.DistanceTo(snappedPoint);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestSnapPoint = snappedPoint;
                    }
                }
            }
        }

        return closestSnapPoint ?? clickPoint;
    }

    private Point2D SnapToDynamicLine(Point2D clickPoint, Point2D lastPoint, out bool shouldBreakSnap)
    {
        shouldBreakSnap = false;

        // If no dynamic line is active, return original point
        if (_dynamicSnapAnchor == null)
            return clickPoint;

        Point2D anchorPoint = _dynamicSnapAnchor.Value;
        double lineAngle = _dynamicSnapAngle;

        // Calculate perpendicular distance from cursor to the dynamic line
        double cos = Math.Cos(lineAngle);
        double sin = Math.Sin(lineAngle);

        // Vector from anchor to cursor
        double vx = clickPoint.X - anchorPoint.X;
        double vy = clickPoint.Y - anchorPoint.Y;

        // Distance along the line (parallel to line)
        double distanceAlongLine = vx * cos + vy * sin;

        // Distance perpendicular to the line
        double perpDistance = Math.Abs(vx * (-sin) + vy * cos);

        // Calculate snap tolerance based on the diagonal line angle
        // For diagonal lines (45 degrees), we need a larger perpendicular tolerance
        // because the diagonal snap interval is ~14.142mm (sqrt(200))
        const double baseSnapTolerance = 10.0; // Base tolerance for perpendicular distance
        double snapTolerance = baseSnapTolerance;

        // Check if we're moving too far perpendicular from the line
        if (perpDistance > snapTolerance)
        {
            shouldBreakSnap = true;
            return clickPoint;
        }

        // Snap the distance along the line using the stored dynamic snap interval
        double snappedDistance = Math.Round(distanceAlongLine / _dynamicSnapInterval) * _dynamicSnapInterval;

        // Count how many snap points away from anchor we are
        int snapCount = (int)Math.Abs(Math.Round(snappedDistance / _dynamicSnapInterval));

        // If we're more than 5 snaps away, signal to break the snap
        if (snapCount > 5)
        {
            shouldBreakSnap = true;
            return clickPoint;
        }

        // Calculate the snapped point on the dynamic line
        Point2D snappedPoint = new Point2D(
            anchorPoint.X + snappedDistance * cos,
            anchorPoint.Y + snappedDistance * sin
        );

        return snappedPoint;
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
        // Handle arrow keys in Move Points mode (only if not typing in dimension box)
        if (_isMovePointsMode && !txtMoveDimension.IsFocused)
        {
            if (e.Key == Key.Up)
            {
                _currentMoveDirection = MoveDirection.Up;
                ResetDirectionButtonColors();
                HighlightDirectionButton(btnMoveUp);
                txtMoveDimension.Focus();
                txtMoveDimension.SelectAll();
                if (_currentMoveDimension != 0) PreviewMove();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Down)
            {
                _currentMoveDirection = MoveDirection.Down;
                ResetDirectionButtonColors();
                HighlightDirectionButton(btnMoveDown);
                txtMoveDimension.Focus();
                txtMoveDimension.SelectAll();
                if (_currentMoveDimension != 0) PreviewMove();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Left)
            {
                _currentMoveDirection = MoveDirection.Left;
                ResetDirectionButtonColors();
                HighlightDirectionButton(btnMoveLeft);
                txtMoveDimension.Focus();
                txtMoveDimension.SelectAll();
                if (_currentMoveDimension != 0) PreviewMove();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Right)
            {
                _currentMoveDirection = MoveDirection.Right;
                ResetDirectionButtonColors();
                HighlightDirectionButton(btnMoveRight);
                txtMoveDimension.Focus();
                txtMoveDimension.SelectAll();
                if (_currentMoveDimension != 0) PreviewMove();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.D && !_isDrawing)
        {
            StartDrawing();
        }
        else if (e.Key == Key.M && !_isDrawing)
        {
            MovePoints_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape && _isMovePointsMode)
        {
            if (_hasPreviewMove)
            {
                // Cancel the current move but stay in move mode
                CancelMove();
            }
            else
            {
                // No active preview, exit move mode completely
                MovePoints_Click(this, new RoutedEventArgs());
            }
            e.Handled = true;
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
        try
        {
            var activeLayer = _currentProject.GetActiveLayer();
            if (lstBusbars.SelectedIndex < 0 || activeLayer == null || lstBusbars.SelectedIndex >= activeLayer.Busbars.Count)
            {
                return;
            }

            // Get the selected busbar from the model (single source of truth)
            var selectedBusbar = activeLayer.Busbars[lstBusbars.SelectedIndex];
            _lastActiveBusbar = selectedBusbar;

            // Highlight the selected busbar (make start/end markers thicker)
            HighlightBusbar(_lastActiveBusbar);

            // Display its name in the textbox (temporarily unhook event to avoid triggering RefreshBusbarList)
            txtBusbarName.TextChanged -= txtBusbarName_TextChanged;
            txtBusbarName.Text = selectedBusbar.Name;
            txtBusbarName.TextChanged += txtBusbarName_TextChanged;

            // Display its segments in the right panel DataGrid (from model)
            dgSegments.ItemsSource = null;
            dgSegments.ItemsSource = selectedBusbar.Segments;

            UpdateStatusBar($"Selected busbar: {selectedBusbar.Name} ({selectedBusbar.Segments.Count} segments)");
        }
        catch (Exception ex)
        {
            UpdateStatusBar($"Error selecting busbar: {ex.Message}");
        }
    }

    private void txtBusbarName_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Skip if project not initialized yet (event fires during XAML initialization)
        if (_currentProject == null)
        {
            return;
        }

        // Only update if a busbar is selected
        int selectedIndex = lstBusbars.SelectedIndex;
        var activeLayer = _currentProject.GetActiveLayer();
        if (selectedIndex < 0 || activeLayer == null || selectedIndex >= activeLayer.Busbars.Count)
        {
            return;
        }

        string newName = txtBusbarName.Text;

        // Update the Busbar model name (single source of truth)
        activeLayer.Busbars[selectedIndex].Name = newName;

        // Also update SavedBusbar for compatibility
        if (selectedIndex < _savedBusbars.Count)
        {
            _savedBusbars[selectedIndex].Name = newName;
        }

        // Refresh the list from the model
        RefreshBusbarList();
        lstBusbars.SelectedIndex = selectedIndex;
    }

    private void HandleBusbarSegmentEdit(DataGridCellEditEndingEventArgs e)
    {
        string columnHeader = e.Column.Header?.ToString() ?? "";

        // Only handle Angle and Length column edits
        if (columnHeader != "Angle (°)" && columnHeader != "Length (mm)")
        {
            return;
        }

        int selectedBusbarIndex = lstBusbars.SelectedIndex;
        var activeLayer = _currentProject?.GetActiveLayer();
        if (selectedBusbarIndex < 0 || activeLayer == null || selectedBusbarIndex >= activeLayer.Busbars.Count)
        {
            return;
        }

        var busbar = activeLayer.Busbars[selectedBusbarIndex];

        // Get the segment index from the row index instead of trying to find the item
        // This avoids issues when the DataGrid is refreshed
        int segmentIndex = e.Row.GetIndex();
        if (segmentIndex < 0 || segmentIndex >= busbar.Segments.Count)
        {
            return;
        }

        var editedSegment = busbar.Segments[segmentIndex];

        // Get the new value from the textbox
        var textBox = e.EditingElement as System.Windows.Controls.TextBox;
        if (textBox == null) return;

        if (!double.TryParse(textBox.Text, out double newValue))
        {
            UpdateStatusBar($"Invalid {(columnHeader == "Angle (°)" ? "angle" : "length")} value.");
            e.Cancel = true;
            return;
        }

        Point2D startPoint = editedSegment.StartPoint;
        Point2D newEndPoint;

        if (columnHeader == "Angle (°)")
        {
            // Editing bend angle
            if (segmentIndex == 0)
            {
                // First segment has no bend before it, angle must be 0
                textBox.Text = "0";
                e.Cancel = true;
                UpdateStatusBar("First segment bend angle is always 0°");
                return;
            }

            // Update the bend angle
            int bendIndex = segmentIndex - 1;
            if (bendIndex >= 0 && bendIndex < busbar.Bends.Count)
            {
                busbar.Bends[bendIndex].Angle = newValue;
                editedSegment.BendAngle = newValue;

                // Calculate new direction: previous segment's direction + bend angle
                var previousSegment = busbar.Segments[segmentIndex - 1];
                double previousAngle = previousSegment.AngleRadians;
                double newAngleRadians = previousAngle + (newValue * Math.PI / 180.0);

                double currentLength = editedSegment.Length;
                newEndPoint = new Point2D(
                    startPoint.X + currentLength * Math.Cos(newAngleRadians),
                    startPoint.Y + currentLength * Math.Sin(newAngleRadians)
                );

                UpdateStatusBar($"Bend angle updated to {newValue:F1}°");
            }
            else
            {
                UpdateStatusBar("Invalid bend index");
                e.Cancel = true;
                return;
            }
        }
        else // Length (mm)
        {
            // Editing length - keep angle the same, change length
            if (newValue <= 0)
            {
                UpdateStatusBar("Invalid length value.");
                e.Cancel = true;
                return;
            }

            // Apply minimum length constraint
            const double minimumSegmentLength = 50.0;
            if (newValue < minimumSegmentLength)
            {
                newValue = minimumSegmentLength;
                textBox.Text = newValue.ToString("F1");
                UpdateStatusBar($"Length adjusted to minimum: {minimumSegmentLength}mm");
            }

            double currentAngle = editedSegment.AngleRadians;

            newEndPoint = new Point2D(
                startPoint.X + newValue * Math.Cos(currentAngle),
                startPoint.Y + newValue * Math.Sin(currentAngle)
            );

            UpdateStatusBar($"Segment {segmentIndex + 1} length updated to {newValue:F1}mm");
        }

        // Move the busbar point - this will update the segment and propagate changes
        // The point index is segmentIndex + 1 (since point 0 is the start of segment 0)
        busbar.MoveBusbarPoint(segmentIndex + 1, newEndPoint);

        // Redraw all busbars (to ensure proper visual update)
        if (_busbarRenderer != null && activeLayer != null)
        {
            _busbarRenderer.RedrawAllBusbars(activeLayer, _lastActiveBusbar);
        }

        // Rebind the DataGrid to avoid stale references
        dgSegments.ItemsSource = null;
        dgSegments.ItemsSource = busbar.Segments;
    }
}