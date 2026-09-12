/*
 * Modal dialog for "Export Script": lets the user pick the output format (executable,
 * Python, PowerShell, batch, or shell script) and the folder downloads should land in
 * before SearchDockpaneViewModel actually writes the kit.
 */
using System.IO;
using System.Windows;

namespace KyFromAboveSTAC
{
    public enum ExportScriptFormat
    {
        Executable,
        Python,
        PowerShell,
        Batch,
        Shell
    }

    public partial class ExportScriptDialog : Window
    {
        public ExportScriptDialog(string defaultDestinationFolder)
        {
            InitializeComponent();
            DestinationBox.Text = defaultDestinationFolder ?? "";
            DestinationBox.Focus();
            DestinationBox.CaretIndex = DestinationBox.Text.Length;
        }

        /// <summary>Which format the user picked. Only meaningful when the dialog returns true.</summary>
        public ExportScriptFormat SelectedFormat
        {
            get
            {
                if (PythonOption.IsChecked == true) return ExportScriptFormat.Python;
                if (PowerShellOption.IsChecked == true) return ExportScriptFormat.PowerShell;
                if (BatchOption.IsChecked == true) return ExportScriptFormat.Batch;
                if (ShellOption.IsChecked == true) return ExportScriptFormat.Shell;
                return ExportScriptFormat.Executable;
            }
        }

        /// <summary>Folder downloads (and the generated script/exe itself) will be written to.</summary>
        public string DestinationFolder => DestinationBox.Text?.Trim()?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder for the downloaded files and the generated script.",
                SelectedPath = string.IsNullOrWhiteSpace(DestinationFolder) ? Path.GetTempPath() : DestinationFolder,
                ShowNewFolderButton = true,
                AutoUpgradeEnabled = true
            };
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                DestinationBox.Text = fbd.SelectedPath;
            }
        }

        private bool ValidateDestination()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                ErrorText.Text = "Enter or browse for a destination folder.";
                ErrorText.Visibility = Visibility.Visible;
                return false;
            }
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDestination()) return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
