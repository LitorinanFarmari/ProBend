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

    private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OK_Click(this, new RoutedEventArgs());
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResponseText = txtInput.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
