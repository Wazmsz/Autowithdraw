using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Telegram.Bot;

namespace Autowithdraw.Global.Common
{
    internal class Logger
    {
        public static List<string> Logs = new List<string>();

        public static void DebugNewBlock(
            BigInteger NewBlock,
            BigInteger Timestamp,
            int ChainID)
        {
            TimeSpan Passed = TimeSpan.FromSeconds(Cast.GetInt(Timestamp) - DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

            string Network = Settings.Chains[ChainID].Name;

            if (Passed.ToString("''h") != "0")
            {
                Debug($"[{Network}]\tCurrent Block: {NewBlock} ({Passed:''h\\:mm\\:ss} hours ago)", ConsoleColor.DarkCyan);
            }
            else if (Passed.ToString("''m") != "0" && (ChainID != 42161 || int.Parse(Passed.ToString("''m")) >= 3))
            {
                Debug($"[{Network}]\tCurrent Block: {NewBlock} ({Passed:''m\\:ss} minutes ago)", ConsoleColor.DarkCyan);
            }
            else if (int.Parse(Passed.ToString("''s")) >= 2 && ChainID != 42161 && ChainID != 1)
            {
                Debug($"[{Network}]\tCurrent Block: {NewBlock} ({Passed:''s} secs ago)", ConsoleColor.DarkCyan);
            }
            else
            {
                Debug($"[{Network}]\tCurrent Block: {NewBlock} (Synced)", ConsoleColor.Cyan);
            }
        }
        public static void Debug(
            string text,
            ConsoleColor color = ConsoleColor.Yellow)
        {
            Print(text, color);
        }

        public static void Error(
            Exception text)
        {
            Print(text.ToString(), ConsoleColor.Red);
        }

        public static void ErrorSpam(
            Exception text,
            string @class)
        {
            PrintSpam($"{@class} - {text.Message}");
        }

        public static void Error(
            string text)
        {
            Print(text, ConsoleColor.Red);
        }

        private static void PrintSpam(
            string text)
        {
            DateTime Date = DateTime.UtcNow.AddHours(3);
            string t = $"[{Date:''HH\\:mm\\:ss}] - {text}";
            File.AppendAllTextAsync("./LogSpam.txt", t + "\n");
        }

        private static Object thisLock = new Object();

        private static async void Print(
            string text,
            ConsoleColor color)
        {
            DateTime Date = DateTime.UtcNow.AddHours(3);
            string t = $"[{Date:''HH\\:mm\\:ss}] - {text}";
            Console.ForegroundColor = color;
            Console.WriteLine(t);

            Logs.Add(t + "\n");

            lock (thisLock)
            {
                File.AppendAllTextAsync("./Log.txt", t + "\n");
            }

            if (text.Contains("not responding") || text.Contains("Timeout") || text.Contains("Operations that change non-concurrent"))
            {
                //await Settings.TXSpy.Bot.SendTextMessageAsync(-1001965805120, "Взрыв.");
                Environment.Exit(0);
            }
        }
    }
}
