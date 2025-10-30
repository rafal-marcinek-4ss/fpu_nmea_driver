using NMEA_FPU_DRIVER.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class Init
    {
        public static string[] BuildSubscriptionList(NmeaDriverConfig cfg)
        {
            var list = new List<string>();

            foreach(var tag in cfg.SubscriptionTags)
            {
                for (int i = 1; i <= tag.Quantity; i++)
                {
                    var fullPath = tag.Path.Replace(tag.ReplaceKeyword, $"{i}");
                    list.Add(fullPath);
                }
            }
            return list.ToArray();
        }
    }
}
