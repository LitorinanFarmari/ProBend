using System.Windows;

namespace BusbarCAD;

public partial class InputDialog : Window
{
    public string ResponseText { get; private set; } = "";

    public InputDialog(string prompt, string defaultValue)
    {
        InitializeComponent();
        txtPrompt.Text = prompt;
        txtInput.Text = defaultValue;
    }

    private void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Focus and select all text when the textbox loads
        txtInput.Focus();
        txtInput.SelectAll();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            // Confirm with Enter
            ResponseText = txtInput.Text;
            DialogResult = true;
            Close();
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Cancel with Escape
            DialogResult = false;
            Close();
        }
    }
}
