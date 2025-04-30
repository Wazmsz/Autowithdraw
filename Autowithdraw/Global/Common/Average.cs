using System;
using System.Collections.Generic;
using System.Text;
using Autowithdraw.Global.Objects;

namespace Autowithdraw.Global.Common
{
    class Average
    {
        private static int USD = 81;

        public static float AllTime(float Total)
        {
            long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1663459200;

            return float.Parse($"{Total * USD / unix:f1}");
        }

        public static float Month(float Total)
        {
            var Date = DateTime.UtcNow.AddHours(3);
            return float.Parse($"{Total * USD / ((86400 * (Date.Day-1)) + (Date.Hour * 3600) + (Date.Minute * 60) + (Date.Second)):f1}");
        }

        public static float Day(float Total)
        {
            var Date = DateTime.UtcNow.AddHours(3);
            return float.Parse($"{Total * USD / ((Date.Hour * 3600) + (Date.Minute * 60) + (Date.Second)):f1}");
        }
    }
}
