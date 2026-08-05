using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace KyFromAbove
{
    public partial class ProgressDialog : Window
    {
        public ProgressDialog(string title = "Progress")
        {
            InitializeComponent();
            Title = title;
            Prog.Visibility = Visibility.Visible;
        }

        /// <summary>Append a line of progress text (thread-safe). Close the window with the X button or Close button.</summary>
        public void Append(string msg)
        {
            if (Dispatcher == null || IsSealed) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (LogBox == null) return;
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                LogBox.ScrollToEnd();
            }), DispatcherPriority.Background);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
