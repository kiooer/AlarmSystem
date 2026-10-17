using System.Windows;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Desktop.Resources;
using Microsoft.Win32;

namespace AlarmSystem.Desktop.Services;

public sealed class LicenseFilePicker : ILicenseFilePicker
{
    public string? SelectLicenseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.LicenseSelectDialogTitle,
            Filter = Strings.LicenseFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public sealed class LicenseImportConfirmationService : ILicenseImportConfirmationService
{
    public bool ConfirmImport(LicenseStatus status) => MessageBox.Show(
        string.Format(
            Strings.LicenseImportConfirmationFormat,
            status.LicenseId,
            status.ExpiresAtUtc?.ToString("u") ?? Strings.NotAvailable,
            string.Join(", ", status.AuthorizedFeatures)),
        Strings.LicenseImportConfirmationTitle,
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;
}

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string value) => Clipboard.SetText(value);
}
