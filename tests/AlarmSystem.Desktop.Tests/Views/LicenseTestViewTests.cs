using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using AlarmSystem.Desktop.Views;

namespace AlarmSystem.Desktop.Tests.Views;

public sealed class LicenseTestViewTests
{
    [Fact]
    public void ReadOnlyTextBoxes_UseOneWayBindings()
    {
        Exception? exception = null;
        BindingMode? machineIdMode = null;
        BindingMode? selectedLicensePathMode = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new LicenseTestView();
                machineIdMode = GetTextBindingMode(view, "MachineIdTextBox");
                selectedLicensePathMode = GetTextBindingMode(view, "SelectedLicensePathTextBox");
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The WPF binding test did not complete.");
        Assert.Null(exception);
        Assert.Equal(BindingMode.OneWay, machineIdMode);
        Assert.Equal(BindingMode.OneWay, selectedLicensePathMode);
    }

    private static BindingMode GetTextBindingMode(LicenseTestView view, string elementName)
    {
        var textBox = Assert.IsType<TextBox>(view.FindName(elementName));
        var binding = BindingOperations.GetBinding(textBox, TextBox.TextProperty);
        return Assert.IsType<Binding>(binding).Mode;
    }
}
