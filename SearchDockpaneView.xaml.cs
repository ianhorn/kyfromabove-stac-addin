/*
 * Code-behind for the KyFromAbove STAC search dockpane view.
 * The ArcGIS Pro framework pairs this UserControl (declared as <content> in
 * Config.daml) with the SearchDockpaneViewModel DockPane instance and sets the
 * DataContext automatically, so no explicit DataContext wiring is needed here.
 */
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
    }
}
