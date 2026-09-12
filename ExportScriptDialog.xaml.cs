/*
 * Modal dialog for "Export Script": lets the user pick the output format (executable,
 * Python, PowerShell, batch, or shell script) and the folder downloads should land in
 * before SearchDockpaneViewModel actually writes the kit.
 */
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
        private static readonly System.Collections.Generic.Dictionary<string, string> FormatHints = new()
        {
            ["Executable"] = "No install needed. Windows only. A single, self-contained .exe -- double-click to run.",
            ["Python"] = "Needs Python 3 on the machine that runs it.",
            ["PowerShell"] = "Windows only, no install needed.",
            ["Batch"] = "Windows only, no install needed (uses curl.exe, bundled with Windows 10/11).",
            ["Shell"] = "macOS/Linux/WSL. Needs curl."
        };

        public ExportScriptDialog(string defaultDestinationFolder)
        {
            InitializeComponent();
            DestinationBox.Text = defaultDestinationFolder ?? "";
            UpdateFormatHint();
            DestinationBox.Focus();
            DestinationBox.CaretIndex = DestinationBox.Text.Length;
        }

        /// <summary>Which format the user picked. Only meaningful when the dialog returns true.</summary>
        public ExportScriptFormat SelectedFormat
        {
            get
            {
                var tag = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string;
                return tag switch
                {
                    "Python" => ExportScriptFormat.Python,
                    "PowerShell" => ExportScriptFormat.PowerShell,
                    "Batch" => ExportScriptFormat.Batch,
                    "Shell" => ExportScriptFormat.Shell,
                    _ => ExportScriptFormat.Executable
                };
            }
        }

        /// <summary>Folder downloads (and the generated script/exe itself) will be written to.</summary>
        public string DestinationFolder => DestinationBox.Text?.Trim()?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFormatHint();

        private void UpdateFormatHint()
        {
            // ComboBoxItem's IsSelected="True" (set in XAML on the default item) fires
            // SelectionChanged synchronously during InitializeComponent(), before later-declared
            // elements like FormatHintText are wired up yet -- guard against that, and rely on
            // the explicit UpdateFormatHint() call after InitializeComponent() in the ctor instead.
            if (FormatHintText == null) return;
            var tag = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Executable";
            FormatHintText.Text = FormatHints.TryGetValue(tag, out var hint) ? hint : "";
        }

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
