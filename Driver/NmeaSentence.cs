using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class NmeaSentence
    {
        public string DeviceName { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Raw { get; set; }
        public bool ChecksumValid { get; set; }
        public string Talker { get; set; }
        public string Type { get; set; }


        public NmeaSentence()
        {
            DeviceName = string.Empty;
            Raw = string.Empty;
            Talker = null;
            Type = null;
        }
    }
}
