using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public static class SystemTimeSetter
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }


        public static bool SetDateTime(DateTime utc)
        {
            SYSTEMTIME st = new SYSTEMTIME
            {
                Year = (ushort)utc.Year,
                Month = (ushort)utc.Month,
                Day = (ushort)utc.Day,
                Hour = (ushort)utc.Hour,
                Minute = (ushort)utc.Minute,
                Second = (ushort)utc.Second,
                Milliseconds = (ushort)utc.Millisecond
            };

            return SetSystemTime(ref st);
        }
    }
}
