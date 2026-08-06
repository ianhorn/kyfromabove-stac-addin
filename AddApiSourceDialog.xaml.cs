/*
 * Modal dialog for the "Bring Your Own API" feature: lets the user point the dockpane at
 * another STAC API, either alongside the built-in KyFromAbove collections (Add) or in place
 * of the whole current source list (Replace all sources).
 */
using System;
using System.Windows;

namespace KyFromAboveSTAC
{
    /// <summary>What the user chose to do with the entered API source, or that they cancelled.</summary>
    public enum AddApiSourceResult
    {
        Cancel,
        Add,
        Replace
    }

    public partial class AddApiSourceDialog : Window
    {
        public AddApiSourceDialog()
        {
            InitializeComponent();
            NameBox.Focus();
        }

        /// <summary>User-entered source name (falls back to the URL if left blank).</summary>
        public string SourceName => NameBox.Text?.Trim();

        /// <summary>User-entered STAC API base URL (no trailing slash).</summary>
        public string BaseUrl => UrlBox.Text?.Trim()?.TrimEnd('/');

        /// <summary>Which button the user clicked. Cancel unless Add/Replace was chosen.</summary>
        public AddApiSourceResult Result { get; private set; } = AddApiSourceResult.Cancel;

        private bool ValidateUrl()
        {
            var url = BaseUrl;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ErrorText.Text = "Enter a valid http(s):// URL for the STAC API base.";
                ErrorText.Visibility = Visibility.Visible;
                return false;
            }
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateUrl()) return;
            Result = AddApiSourceResult.Add;
            DialogResult = true;
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateUrl()) return;
            Result = AddApiSourceResult.Replace;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = AddApiSourceResult.Cancel;
            DialogResult = false;
        }
    }
}
