/*
 * Code-behind for the KyFromAbove STAC search dockpane view.
 * The ArcGIS Pro framework pairs this UserControl (declared as <content> in
 * Config.daml) with the SearchDockpaneViewModel DockPane instance and sets the
 * DataContext automatically, so no explicit DataContext wiring is needed here.
 */
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace KyFromAboveSTAC
{
    /// <summary>
    /// Interaction logic for SearchDockpaneView.xaml
    /// </summary>
    public partial class SearchDockpaneView : UserControl
    {
        // Raw-JSON hover card. A WPF ToolTip closes as soon as the mouse leaves its owner, which made
        // the card's scrollbar unreachable, so it's a shared Popup instead: it opens after a short
        // hover on a result row, stays open while the mouse is over the row OR the card, and closes
        // shortly after the mouse leaves both.
        private static readonly System.TimeSpan JsonShowDelay = System.TimeSpan.FromMilliseconds(500);
        private static readonly System.TimeSpan JsonHideDelay = System.TimeSpan.FromMilliseconds(300);
        private readonly DispatcherTimer _jsonShowTimer = new() { Interval = JsonShowDelay };
        private readonly DispatcherTimer _jsonHideTimer = new() { Interval = JsonHideDelay };
        private FrameworkElement _pendingJsonRow;
        private FrameworkElement _openJsonRow;

        public SearchDockpaneView()
        {
            InitializeComponent();

            if (ProTheme.IsDark)
                LoadCollectionsButton.Style = (Style)Resources["DarkThemeButtonStyle"];

            PanelGrid.LayoutUpdated += (_, _) => FitResultsList();
            PanelScroll.SizeChanged += (_, _) => FitResultsList();

            _jsonShowTimer.Tick += (_, _) =>
            {
                _jsonShowTimer.Stop();
                if (_pendingJsonRow != null) ShowJson(_pendingJsonRow);
            };
            _jsonHideTimer.Tick += (_, _) =>
            {
                _jsonHideTimer.Stop();
                HideJson();
            };
            Unloaded += (_, _) =>
            {
                _jsonShowTimer.Stop();
                _jsonHideTimer.Stop();
                HideJson();
            };
        }

        // The whole panel scrolls (PanelScroll), but the results list keeps its own scrollbar. Inside a ScrollViewer
        // the list would otherwise be measured with unlimited height and grow to fit every result, so cap it at
        // whatever is left of the viewport after everything else in the panel -- never below a usable minimum,
        // in which case the panel scrolls instead.
        private const double MinResultsListHeight = 260;

        private void FitResultsList()
        {
            if (PanelScroll.ViewportHeight <= 0 || ResultsScroll.ActualHeight <= 0) return;

            // Sum the fixed-height rows directly. Subtracting the list from the whole grid would also count the
            // empty space around a list that is currently capped smaller than its row, and it could never grow back.
            var others = PanelGrid.Margin.Top + PanelGrid.Margin.Bottom;
            for (var i = 0; i < PanelGrid.RowDefinitions.Count; i++)
                if (i != Grid.GetRow(ResultsGrid)) others += PanelGrid.RowDefinitions[i].ActualHeight;
            for (var i = 0; i < ResultsGrid.RowDefinitions.Count; i++)
                if (i != Grid.GetRow(ResultsScroll)) others += ResultsGrid.RowDefinitions[i].ActualHeight;

            var target = System.Math.Max(MinResultsListHeight, PanelScroll.ViewportHeight - others);
            // Only reassign on a real change: setting MaxHeight relayouts, which raises LayoutUpdated again.
            if (double.IsInfinity(ResultsScroll.MaxHeight) || System.Math.Abs(ResultsScroll.MaxHeight - target) > 1)
                ResultsScroll.MaxHeight = target;
        }

        // A list that has nothing left to scroll in the wheel's direction hands the wheel to the whole panel;
        // otherwise a ScrollViewer swallows it and the panel can't be wheel-scrolled while the mouse is over a list.
        private void InnerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var inner = (ScrollViewer)sender;
            var atTop = inner.VerticalOffset <= 0;
            var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
            if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
            {
                PanelScroll.ScrollToVerticalOffset(PanelScroll.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ResultRow_MouseEnter(object sender, MouseEventArgs e)
        {
            var row = (FrameworkElement)sender;
            _jsonHideTimer.Stop(); // moving between the row and the card must not close it
            if (row == _openJsonRow) return;
            _pendingJsonRow = row;
            _jsonShowTimer.Stop();
            _jsonShowTimer.Start();
        }

        private void ResultRow_MouseLeave(object sender, MouseEventArgs e)
        {
            _jsonShowTimer.Stop();
            _pendingJsonRow = null;
            ScheduleJsonHide();
        }

        private void ResultRow_Unloaded(object sender, RoutedEventArgs e)
        {
            // A cleared/re-searched result list removes rows without a MouseLeave.
            if (sender == _openJsonRow || sender == _pendingJsonRow)
            {
                _jsonShowTimer.Stop();
                _pendingJsonRow = null;
                HideJson();
            }
        }

        private void JsonCard_MouseEnter(object sender, MouseEventArgs e) => _jsonHideTimer.Stop();

        private void JsonCard_MouseLeave(object sender, MouseEventArgs e) => ScheduleJsonHide();

        private void ScheduleJsonHide()
        {
            if (_openJsonRow == null) return;
            // Dragging out of the card while selecting text must not close it; JsonText_LostMouseCapture reschedules the hide.
            if (JsonText.IsMouseCaptured) return;
            _jsonHideTimer.Stop();
            _jsonHideTimer.Start();
        }

        private void JsonText_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!JsonCard.IsMouseOver) ScheduleJsonHide();
        }

        private void ShowJson(FrameworkElement row)
        {
            if (row.DataContext is not ResultItemViewModel item) return;
            JsonPopup.IsOpen = false; // re-anchor if it was showing another row
            JsonText.Document = BuildJsonDocument(item.DetailJson);
            JsonText.ScrollToHome();
            JsonPopup.PlacementTarget = row;
            JsonPopup.IsOpen = true;
            _openJsonRow = row;
        }

        // URLs in the JSON stop at a quote, whitespace, or backslash.
        private static readonly Regex JsonUrlPattern = new(@"https?://[^\s""\\<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>The item's JSON as selectable text, with each http(s) URL turned into a clickable link.</summary>
        private FlowDocument BuildJsonDocument(string json)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            var pos = 0;
            foreach (Match m in JsonUrlPattern.Matches(json))
            {
                if (m.Index > pos) paragraph.Inlines.Add(new Run(json.Substring(pos, m.Index - pos)));
                if (Uri.TryCreate(m.Value, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    var link = new Hyperlink(new Run(m.Value)) { NavigateUri = uri, ToolTip = "Open in your browser" };
                    link.RequestNavigate += JsonLink_RequestNavigate;
                    paragraph.Inlines.Add(link);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(m.Value));
                }
                pos = m.Index + m.Length;
            }
            if (pos < json.Length) paragraph.Inlines.Add(new Run(json.Substring(pos)));
            return new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        }

        private void JsonLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open the link:\n{ex.Message}", "KyFromAbove-STAC", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HideJson()
        {
            JsonPopup.IsOpen = false;
            _openJsonRow = null;
        }

        // "Parallel Downloads" and "Draw..." are fixed-label dropdown buttons (Button + Popup/ListBox)
        // rather than a normal ComboBox, so their text doesn't change to show the current selection.
        // A plain Button here (not ToggleButton) because Pro skins Button automatically to match the
        // rest of the panel, but has no equivalent skin for ToggleButton -- using one made "Draw..."
        // visibly lighter than its neighbors. Click toggles Popup.IsOpen directly off the current
        // value (not unconditionally to true), so clicking again while open closes it, and an
        // outside-click light-dismiss (StaysOpen="False") closes it without leaving any stale state
        // for the next click to fight with.
        private void ParallelDownloadsButton_Click(object sender, RoutedEventArgs e) => ParallelDownloadsPopup.IsOpen = !ParallelDownloadsPopup.IsOpen;

        private void ParallelDownloadsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ParallelDownloadsPopup.IsOpen = false;
        }

        private void DrawAoiButton_Click(object sender, RoutedEventArgs e) => DrawAoiPopup.IsOpen = !DrawAoiPopup.IsOpen;

        // Same fixed-label dropdown pattern as above, offering the three Draw* AOI tools;
        // clicking an option runs its command (bound normally) and then closes the popup.
        private void DrawAoiOption_Click(object sender, RoutedEventArgs e)
        {
            DrawAoiPopup.IsOpen = false;
        }
    }
}
