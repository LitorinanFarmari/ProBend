using System.Windows;
using BusbarCAD.Models;
using BusbarCAD.Calculations;

namespace BusbarCAD.UI
{
    public partial class DimensionDialog : Window
    {
        private Busbar _busbar;
        private int _segmentIndex;
        private double _currentDimension;

        public double NewDimension { get; private set; }

        public DimensionDialog(Busbar busbar, int segmentIndex, double currentDimension)
        {
            InitializeComponent();
            _busbar = busbar;
            _segmentIndex = segmentIndex;
            _currentDimension = currentDimension;

            txtCurrent.Text = $"Current: {currentDimension:F1}mm";
            txtDimension.Text = currentDimension.ToString("F1");
            txtDimension.SelectAll();
            txtDimension.Focus();
        }

        private void TxtDimension_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (double.TryParse(txtDimension.Text, out double value))
            {
                // Create a temporary copy of the busbar to test validation
                var testBusbar = CloneBusbarWithNewDimension(value);
                var validationResult = ValidationEngine.ValidateBusbar(testBusbar);

                if (!validationResult.IsValid)
                {
                    txtValidation.Text = string.Join("\n", validationResult.Errors);
                    txtValidation.Visibility = Visibility.Visible;
                    btnOK.IsEnabled = false;
                }
                else
                {
                    txtValidation.Visibility = Visibility.Collapsed;
                    btnOK.IsEnabled = true;
                }
            }
            else
            {
                txtValidation.Text = "Invalid number";
                txtValidation.Visibility = Visibility.Visible;
                btnOK.IsEnabled = false;
            }
        }

        private Busbar CloneBusbarWithNewDimension(double newDimension)
        {
            // Create a simple clone for validation purposes
            var clone = new Busbar(_busbar.Name);

            for (int i = 0; i < _busbar.Segments.Count; i++)
            {
                var seg = _busbar.Segments[i];
                double length = (i == _segmentIndex) ? newDimension : seg.InsideLength;

                var newSegment = new Segment(seg.StartPoint, seg.EndPoint, length);
                clone.AddSegment(newSegment);
            }

            return clone;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(txtDimension.Text, out double value))
            {
                NewDimension = value;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
