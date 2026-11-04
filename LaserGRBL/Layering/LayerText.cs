using System.Globalization;
using System.Resources;

namespace LaserGRBL.Layering
{
    internal static class LayerText
    {
        private static readonly ResourceManager Manager = new ResourceManager("LaserGRBL.Layering.LayerText", typeof(LayerText).Assembly);
        public static string Get(string key) { return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key; }
    }
}
