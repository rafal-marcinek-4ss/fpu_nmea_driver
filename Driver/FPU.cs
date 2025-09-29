using NMEA_FPU_DRIVER.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;


namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class FPU
    {
        public string Name { get; set; }
        public float Easting { get; set; }
        public float Northing { get; set; }
        public float Heading { get; set; }
        public float Pitch { get; set; }
        public float Roll { get; set; }
        public float GpsQuality { get; set; }
        public int TimeSyncActiveInterface { get; set; } = 0;
        public string SyncTimestring { get; set; }
        public DateTimeOffset DateTime { get; set; }
        public DateTimeOffset SyncDateTime { get; set; }

        private Logger _log = LogManager.GetCurrentClassLogger();
        public FPU(string name)
        {
            Name = name;
        }

        public void ParseTSS1(string message)
        {
            Regex Rx = new Regex(
                @"^:(?<aa>\d{2})(?<bbbb>\d{4})\s" +
                @"(?<heaveSign>[+-])(?<heave>\d{4})x" +
                @"(?<rollSign>[+-])(?<roll>\d{4})\s" +
                @"(?<pitchSign>[+-])(?<pitch>\d{4})" +
                @"\.?\r?\n?$",
                RegexOptions.Compiled);

            var match = Rx.Match(message);

            if (!match.Success) { _log.Warn($"Error parsing TTS1 message {Name}"); return; }

            int rollRaw = int.Parse(match.Groups["roll"].Value, CultureInfo.InvariantCulture);
            Roll = rollRaw / 100f;
            if (match.Groups["rollSign"].Value == "-") Roll = -Roll;

            int pitchRaw = int.Parse(match.Groups["pitch"].Value, CultureInfo.InvariantCulture);
            Pitch = pitchRaw / 100f;
            if (match.Groups["pitchSign"].Value == "-") Pitch = -Pitch;
        }


        public void ParseHDT(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) { _log.Warn($"ERROR PARSING HDT MESSAGE {Name}"); return; }
            msg = msg.Trim();

            int star = msg.IndexOf('*');
            if (!msg.StartsWith("$") || star < 0 || star > msg.Length - 3) { _log.Warn($"ERROR PARSING HDT MESSAGE {Name}"); return; }

            string payload = msg.Substring(1, star - 1);
            var parts = payload.Split(',');
            if (parts.Length < 2) { _log.Warn($"ERROR PARSING HDT MESSAGE {Name}"); return; }

            bool ok;
            ok = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float heading);

            if (!ok) { _log.Warn($"ERROR PARSING HDT MESSAGE {Name}"); return; }

            if (heading < 0f) heading = (heading % 360f + 360f) % 360f;
            if (heading >= 360f) heading = heading % 360f;

            Heading = heading;
        }

        public void ParseGGA(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) { _log.Warn($"ERROR PARSING GGA MESSAGE {Name}"); return; }
            msg = msg.Trim();

            int star = msg.IndexOf('*');
            if (!msg.StartsWith("$") || star < 0 || star > msg.Length - 3) { _log.Warn($"ERROR PARSING GGA MESSAGE {Name}"); return; }

            string payload = msg.Substring(1, star - 1);
            var parts = payload.Split(',');
            if (parts.Length < 7) { _log.Warn($"ERROR PARSING GGA MESSAGE {Name}"); return; }

            //LATITUDE
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double latRaw))
            {
                _log.Warn($"ERROR PARSING LAT {Name}");
                return;
            }
            string latHem = parts[3];

            //LONGITUDE
            if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lonRaw))
            {
                _log.Warn($"ERROR PARSING LON {Name}");
                return;
            }
            string lonHem = parts[5];

            int quality = 0;
            int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out quality);


            double latDeg = Math.Floor(latRaw / 100);
            double latMin = latRaw - latDeg * 100;
            double latitude = latDeg + (latMin / 60.0);
            if (latHem == "S") latitude = -latitude;

            double lonDeg = Math.Floor(lonRaw / 100);
            double lonMin = lonRaw - lonDeg * 100;
            double longitude = lonDeg + (lonMin / 60.0);
            if (lonHem == "W") longitude = -longitude;

            //(double easting, double northing) = Helpers.ToWebMercator(longitude, latitude);
            (double easting, double northing, int zone, bool isNorth) = Helpers.ToUTM(longitude, latitude);

            Easting = (float)easting;
            Northing = (float)northing;

            GpsQuality = quality;

        }


        public void ParseZDA(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }
            msg = msg.Trim();

            int star = msg.IndexOf('*');
            if (!msg.StartsWith("$") || star < 0 || star > msg.Length - 3) { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }

            string payload = msg.Substring(1, star - 1);
            var parts = payload.Split(',');
            if (parts.Length < 7) { _log.Warn($"ERROR PARSING GGA MESSAGE {Name}"); return; }

            if (!TryParseUtcHhmmss(parts[1], out int hh, out int mm, out int ss, out int ms))
            { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }

            int day, month, year;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)) { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)) { _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return; }

            try
            {
                var utc = new DateTime(year, month, day, hh, mm, ss, ms, DateTimeKind.Utc);

                TimeSpan? offset = null;
                DateTimeOffset? local = null;

                int offH, offM;
                bool hasHr = !string.IsNullOrEmpty(parts[5]);
                bool hasMin = parts.Length >= 7 && !string.IsNullOrEmpty(parts[6]);

                if (hasHr && hasMin &&
                int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out offH) &&
                int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out offM))
                {
                    int sign = offH < 0 ? -1 : 1;   // znak na godzinach wyznacza kierunek
                    offH = Math.Abs(offH);
                    offM = Math.Abs(offM);
                    offset = new TimeSpan(sign * offH, sign * offM, 0);

                    local = new DateTimeOffset(utc.Ticks, TimeSpan.Zero).ToOffset(offset.Value);

                    Console.WriteLine($"TIME: {local}");
                }

            }
            catch
            {
                _log.Warn($"ERROR PARSING ZDA MESSAGE {Name}"); return;
            }

        }

        private static bool TryParseUtcHhmmss(string s, out int hh, out int mm, out int ss, out int ms)
        {
            hh = mm = ss = ms = 0;
            if (string.IsNullOrWhiteSpace(s) || s.Length < 6) return false;

            if (!int.TryParse(s.Substring(0, 2), out hh)) return false;
            if (!int.TryParse(s.Substring(2, 2), out mm)) return false;


            string secPart = s.Substring(4);
            double secD;
            if (!double.TryParse(secPart, NumberStyles.Float, CultureInfo.InvariantCulture, out secD))
                return false;

            ss = (int)Math.Floor(secD);
            ms = (int)Math.Round((secD - ss) * 1000.0, MidpointRounding.AwayFromZero);
            if (ms == 1000) { ms = 0; ss += 1; }
            if (ss >= 60) { ss -= 60; mm += 1; }
            if (mm >= 60) { mm -= 60; hh += 1; }
            if (hh >= 24) hh -= 24; 

            return (hh >= 0 && hh < 24) && (mm >= 0 && mm < 60) && (ss >= 0 && ss < 60);
        }

    }
    
    public static class FpuFactory
    {
        public static Dictionary<string, FPU> CreateDevice(NmeaDriverConfig config)
        {
            var devices = new Dictionary<string, FPU>();

            foreach (var devCfg in config.Devices)
            {
                var device = new FPU(devCfg.Name);

                devices[devCfg.Name] = device;
            }

            return devices;
        }
    }

}
