using Autowithdraw.Global.Common;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace Autowithdraw.Global.Objects
{
    class ConfigParse
    {
        public string Recipient { get; set; }
        public Proxy Proxy { get; set; }
        public int Wei { get; set; }
        public Telegram Telegram { get; set; }
        public string Path { get; set; }
        //public string DripsPath { get; set; }
        public List<Net> Chains { get; set; }
        public Other Other { get; set; }

        public void Save()
        {
            File.WriteAllText("./config.json", JsonSerializer.Serialize(Settings.Config,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
    }

    class Proxy
    {
        public string Address { get; set; }
        public string PrivateKey { get; set; }
    }

    class Telegram
    {
        public string TXSpy { get; set; }
        public string Profits { get; set; }
        public List<string> Allowed { get; set; }
    }

    class Net
    {
        public string API { get; set; }
        public string HttpPort { get; set; }
        public string WssPort { get; set; }
        public int ID { get; set; }
        public ContractObject Contract { get; set; }
    }

    class ContractObject
    {
        public string ABI { get; set; }
        public string Sponsor { get; set; }
        public string ContractAddress { get; set; }
        public string PrivateKey { get; set; }
    }

    class Other
    {
        public float Minimum { get; set; }
        public bool StoppedAW { get; set; }
        public List<string> RPCList { get; set; }
        public List<string> ScamTokens { get; set; }
        public Dictionary<string, TrustedToken> TrustedTokens { get; set; }
    }

    class TrustedToken
    {
        public int ChainID { get; set; }
        public string Symbol { get; set; }
    }

    class InchSwap
    {
        public string toTokenAmount { get; set; }
    }

    class Stakes
    {
        public string Address { get; set; }
        public string ContractAddress { get; set; }
        public string Name { get; set; }
        public int Amount { get; set; }
        public int Gwei { get; set; }
    }

    class ResultStake
    {
        public BigInteger Amount { get; set; }
        public int End { get; set; }
    }

    [FunctionOutput]
    class userDeposits : IFunctionOutputDTO
    {
        [Parameter("int256", "", 1)]
        public virtual BigInteger Amount { get; set; }
        [Parameter("int256", "", 2)]
        public virtual int noname_1 { get; set; }
        [Parameter("int256", "", 3)]
        public virtual int End { get; set; }
        [Parameter("int256", "", 4)]
        public virtual BigInteger noname_2 { get; set; }
        [Parameter("int256", "", 5)]
        public virtual BigInteger noname_3 { get; set; }

        [Parameter("bool", "", 6)]
        public virtual bool noname_4 { get; set; }
    }

    [FunctionOutput]
    class userInfo : IFunctionOutputDTO
    {
        [Parameter("int256", "shares", 1)]
        public virtual BigInteger noname_1 { get; set; }
        [Parameter("int256", "lastDepositedTime", 2)]
        public virtual BigInteger noname_2 { get; set; }
        [Parameter("int256", "cakeAtLastUserAction", 3)]
        public virtual BigInteger noname_3 { get; set; }
        [Parameter("int256", "lastUserActionTime", 4)]
        public virtual BigInteger noname_4 { get; set; }
        [Parameter("int256", "lockStartTime", 5)]
        public virtual BigInteger noname_5 { get; set; }

        [Parameter("int256", "lockEndTime", 6)]
        public virtual int End { get; set; }

        [Parameter("int256", "userBoostedShare", 7)]
        public virtual BigInteger noname_7 { get; set; }

        [Parameter("bool", "locked", 8)]
        public virtual bool noname_8 { get; set; }
        [Parameter("int256", "lockedAmount", 9)]
        public virtual BigInteger noname_9 { get; set; }
    }

    class Ether
    {
        public string ContractAddress { get; set; }
        public int Timestamp { get; set; }
    }

    class Stats
    {
        public Date Day { get; set; }
        public Date Month { get; set; }
        public Date AllTime { get; set; }

        #region Spend

        public void AddTotalSpend(float value)
        {
            Day.Spends.Total += value;
            Month.Spends.Total += value;
            AllTime.Spends.Total += value;
        }
        public void AddSponsoredSpend(float value)
        {
            Day.Spends.Sponsored += value;
            Month.Spends.Sponsored += value;
            AllTime.Spends.Sponsored += value;
            AddTotalSpend(value);
        }
        public void AddFlashbotsSpend(float value)
        {
            Day.Spends.Flashbots += value;
            Month.Spends.Flashbots += value;
            AllTime.Spends.Flashbots += value;
            AddTotalSpend(value);
        }
        public void AddProxySpend(float value)
        {
            Day.Spends.Proxy += value;
            Month.Spends.Proxy += value;
            AllTime.Spends.Proxy += value;
            AddTotalSpend(value);
        }

        #endregion

        #region Earn

        public void AddTotal(float value)
        {
            Day.Earns.Total += value;
            Month.Earns.Total += value;
            AllTime.Earns.Total += value;
        }
        public void AddFlashbots(float value)
        {
            Day.Earns.Flashbots += value;
            Month.Earns.Flashbots += value;
            AllTime.Earns.Flashbots += value;
            AddTotal(value);
        }
        public void AddNative(float value, bool isSmartGas = false)
        {
            if (isSmartGas)
            {
                Day.Earns.SmartGas.Native += value;
                Month.Earns.SmartGas.Native += value;
                AllTime.Earns.SmartGas.Native += value;
            }
            else
            {
                Day.Earns.Withdraw.Native += value;
                Month.Earns.Withdraw.Native += value;
                AllTime.Earns.Withdraw.Native += value;
            }

            AddTotal(value);
        }
        public void AddTokens(float value, bool isSmartGas = false)
        {
            if (isSmartGas)
            {
                Day.Earns.SmartGas.Tokens += value;
                Month.Earns.SmartGas.Tokens += value;
                AllTime.Earns.SmartGas.Tokens += value;
            }
            else
            {
                Day.Earns.Withdraw.Tokens += value;
                Month.Earns.Withdraw.Tokens += value;
                AllTime.Earns.Withdraw.Tokens += value;
            }

            AddTotal(value);
        }

        #endregion

        public void Save()
        {
            File.WriteAllText("./stats.json", JsonSerializer.Serialize(Settings.Stats,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
    }

    class Date
    {
        public Spends Spends { get; set; }
        public Earn Earns { get; set; }

        public void Reset()
        {
            Spends.Reset();
            Earns.Reset();
        }
    }

    class Spends
    {
        public float Total { get; set; }
        public string TotalCultured() => Helper.SplitInt(Total);

        public float Sponsored { get; set; }
        public string SponsoredCultured() => Helper.SplitInt(Sponsored);

        public float Flashbots { get; set; }
        public string FlashbotsCultured() => Helper.SplitInt(Flashbots);

        public float Proxy { get; set; }
        public string ProxyCultured() => Helper.SplitInt(Proxy);

        public void Reset()
        {
            Total = 0;
            Sponsored = 0;
            Flashbots = 0;
            Proxy = 0;
        }
    }

    class Earn
    {
        public AllTokens Withdraw { get; set; }
        public AllTokens SmartGas { get; set; }

        public float Total { get; set; }
        public string TotalCultured() => Helper.SplitInt(Total);

        public float Flashbots { get; set; }
        public string FlashbotsCultured() => Helper.SplitInt(Flashbots);

        public string SmartGasTotal()
        {
            return Helper.SplitInt(SmartGas.Native + SmartGas.Tokens);
        }

        public string WithdrawTotal()
        {
            return Helper.SplitInt(Withdraw.Native + Withdraw.Tokens);
        }

        public void Reset()
        {
            Total = 0;
            Flashbots = 0;
            Withdraw.Reset();
            SmartGas.Reset();
        }
    }

    class AllTokens
    {
        public float Native { get; set; }
        public string NativeCultured() => Helper.SplitInt(Native);


        public float Tokens { get; set; }
        public string TokensCultured() => Helper.SplitInt(Tokens);

        public void Reset()
        {
            Native = 0;
            Tokens = 0;
        }
    }

    class Chain
    {
        public Chain()
        {
            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        Price = Network.Get<Dictionary<string, float>>($"https://min-api.cryptocompare.com/data/price?fsym={Token}&tsyms=USD&api_key=f9216f4f74a6f207ed6df263aa53cfdad149a2f13e7d338d45d343f9075c571e")["USD"];
                        Logger.Debug($"{Token}: {Price}$");
                        break;
                    }
                    catch (Exception e)
                    {
                        Thread.Sleep(1000 * 5);
                    }
                }
            }).Start();
        }
        public string USDT { get; set; }
        public ContractObject Contract = new ContractObject();
        public BigInteger Decimals = 1000000;
        public BigInteger DefaultGas = 21000;
        public IClient HTTPClient { get; set; }
        public string Name { get; set; }
        public string Token { get; set; }
        public string API = "None";
        public string Dir = "";
        public string WSSDir = "";
        public Web3 Web3 { get; set; }
        public string WSS { get; set; }
        public string Link { get; set; }
        public float Price;
        public bool LowGwei = false;
    }

    internal class TransactionDescription
    {
        public BigInteger GasPrice { get; set; }
        public BigInteger Nonce { get; set; }
    }

    internal class Bundle
    {
        public string jsonrpc { get; set; }
        public int id { get; set; }
        public string method { get; set; }
        public @params[] @params { get; set; }
    }

    internal class @params
    {
        public List<string> txs { get; set; }
        public string? blockNumber { get; set; }
        public long? maxTimestamp { get; set; }
    }

    internal class RandomKey
    {
        public string Address { get; set; }
        public string PrivateKey { get; set; }
    }
}
