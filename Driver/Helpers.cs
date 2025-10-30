using NMEA_FPU_DRIVER.Config;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public static class Helpers
    {
        public static async Task Quiet(Task t)
        {
            try { await t.ConfigureAwait(false); } catch { }
        }

        public static bool ValidateCheckSum(string line, out string talker, out string type)
        {
            talker = null; type = null;
            var starIdx = line.LastIndexOf('*');
            if (starIdx <= 0 || starIdx + 3 > line.Length) return false;

            var payload = line.Substring(1, starIdx - 1);
            var sumStr = line.Substring(starIdx + 1, 2);

            int calc = 0;
            for (int i = 0; i < payload.Length; i++) calc ^= (byte)payload[i];
            var calcStr = calc.ToString("X2");
            bool ok = string.Equals(sumStr, calcStr, StringComparison.Ordinal);

            if (payload.Length > 5)
            {
                talker = payload.Substring(0, 2);
                var len = Math.Min(3, payload.Length - 2);
                type = payload.Substring(2, len);
            }
            return ok;
         
        }

        public static (double X, double Y) ToWebMercator(double lon, double lat)
        {
            var csFactory = new CoordinateSystemFactory();
            var ctFactory = new CoordinateTransformationFactory();

            var wgs84 = GeographicCoordinateSystem.WGS84;
            var webMercator = ProjectedCoordinateSystem.WebMercator;

            var transform = ctFactory.CreateFromCoordinateSystems(wgs84, webMercator);

            double[] point = new[] { lon, lat };
            double[] result = transform.MathTransform.Transform(point);

            return (result[0], result[1]);
        }

        public static (double Easting, double Northing, int Zone, bool IsNorthern) ToUTM(double lon, double lat)
        {
            var csFactory = new CoordinateSystemFactory();
            var ctFactory = new CoordinateTransformationFactory();

            var wgs84 = GeographicCoordinateSystem.WGS84;
            int utmZone = (int)Math.Floor((lon + 180) / 6) + 1;
            bool isNorthern = lat >= 0;

            var utm = ProjectedCoordinateSystem.WGS84_UTM(utmZone, isNorthern);

            var transform = ctFactory.CreateFromCoordinateSystems(wgs84, utm);

            double[] point = new[] { lon, lat };
            double[] result = transform.MathTransform.Transform(point);

            return (result[0], result[1], utmZone, isNorthern);

        }

        public static string GetWriteTagPath(IEnumerable<WriteTag> allWriteTags, string name, string side)
        {
            if (allWriteTags == null || string.IsNullOrWhiteSpace(name)) return null;

            var tag = allWriteTags.FirstOrDefault(t =>
                t != null && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (tag == null) return null;

            var path = tag.Path ?? string.Empty;

            path = path.Replace(tag.ReplaceKeyword, side);

            return path;
        }

    }
}
