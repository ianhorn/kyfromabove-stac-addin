/*
 * KyFromAbove STAC Browser - ArcGIS Pro Add-in
 * Module entry point (singleton). Holds shared services.
 */
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace KyFromAboveSTAC
{
    /// <summary>
    /// Add-in module. Provides access to shared services (e.g. the STAC client).
    /// </summary>
    internal class Module1 : Module
    {
        private static Module1 _this = null;

        /// <summary>
        /// Retrieve the module instance.
        /// </summary>
        public static Module1 Current
        {
            get
            {
                if (_this == null)
                    _this = (Module1)FrameworkApplication.FindModule("KyFromAbove_Module");
                return _this;
            }
        }

        /// <summary>
        /// Shared STAC API client (single HttpClient-backed instance).
        /// </summary>
        public Stac.StacClient StacClient { get; } = new Stac.StacClient();
    }
}
