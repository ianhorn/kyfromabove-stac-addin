/*
 * Reads ArcGIS Pro's current application theme. Pro's theme resources aren't available to this
 * add-in's XAML at runtime (see the DesignOnlyResourceDictionary note in SearchDockpaneView.xaml),
 * and switching themes needs a Pro restart, so callers can check this once when a view is created.
 */
using System;
using System.Runtime.CompilerServices;
using ArcGIS.Desktop.Framework;

namespace KyFromAboveSTAC
{
    internal static class ProTheme
    {
        /// <summary>True when Pro is in its Dark theme. False for every other theme, or when Pro's framework isn't available.</summary>
        public static bool IsDark
        {
            get
            {
                try { return QueryDark(); }
                catch (Exception) { return false; }
            }
        }

        // Separate, non-inlined method so a missing Pro assembly surfaces inside IsDark's try block.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool QueryDark() => FrameworkApplication.ApplicationTheme == ApplicationTheme.Dark;
    }
}
