/*
 * Ribbon button for the Feedback dialog -- see FeedbackDialog.xaml.
 */
using ArcGIS.Desktop.Framework.Contracts;

namespace KyFromAboveSTAC
{
    internal class FeedbackButton : Button
    {
        protected override void OnClick() =>
            new FeedbackDialog { Owner = System.Windows.Application.Current?.MainWindow }.ShowDialog();
    }
}
