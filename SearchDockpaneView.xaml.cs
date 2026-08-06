/*
 * Code-behind for the KyFromAbove STAC search dockpane view.
 * The ArcGIS Pro framework pairs this UserControl (declared as <content> in
 * Config.daml) with the SearchDockpaneViewModel DockPane instance and sets the
 * DataContext automatically, so no explicit DataContext wiring is needed here.
 */
using System.Windows;
using System.Windows.Controls;

namespace KyFromAboveSTAC
{
    /// <summary>
    /// Interaction logic for SearchDockpaneView.xaml
    /// </summary>
    public partial class SearchDockpaneView : UserControl
    {
        public SearchDockpaneView()
        {
            InitializeComponent();
        }

        // "Parallel Downloads" is a fixed-label dropdown button (ToggleButton + Popup/ListBox)
        // rather than a normal ComboBox, so its text doesn't change to show the current selection.
        // Popup.IsOpen is bound directly to the ToggleButton's IsChecked (two-way), so clicking the
        // button toggles it open/closed like a real dropdown, and an outside-click light-dismiss
        // (StaysOpen="False") correctly resets the button back to unchecked instead of leaving it
        // looking "stuck" -- the previous Button+Click-only-sets-IsOpen=true version never did.
        private void ParallelDownloadsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ParallelDownloadsPopup.IsOpen = false;
        }

        // Same fixed-label dropdown pattern as above, offering the three Draw* AOI tools;
        // clicking an option runs its command (bound normally) and then closes the popup.
        private void DrawAoiOption_Click(object sender, RoutedEventArgs e)
        {
            DrawAoiPopup.IsOpen = false;
        }
    }
}
