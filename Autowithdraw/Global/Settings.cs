using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Autowithdraw.Main.Actions;
using NBitcoin;
using Nethereum.Contracts;
using Nethereum.HdWallet;
using Nethereum.JsonRpc.Client;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autowithdraw.Main.Handlers;
using Org.BouncyCastle.Asn1.Cms;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Autowithdraw.Global
{
    internal class Settings
    {
        public static Dictionary<int, Chain> Chains = new Dictionary<int, Chain>
        {
            {1, new Chain {
                USDT = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
                Name = "Ethereum",
                Token = "ETH",
                Link = "etherscan.io"
            }},
            {56, new Chain {
                USDT = "0xc2132D05D31c914a87C6611C10748AEb04B58e8F",
                Name = "BSC",
                Token = "BNB",
                Link = "bscscan.com"
            }},
            {137, new Chain {
                USDT = "0xc2132D05D31c914a87C6611C10748AEb04B58e8F",
                Name = "Polygon",
                Token = "MATIC",
                Link = "polygonscan.com"
            }},
            {128, new Chain {
                USDT = "0xa71EdC38d189767582C38A3145b5873052c3e47a",
                Decimals = 1000000000000000000,
                Name = "Heco",
                Token = "HT",
                Link = "hecoinfo.com"
            }},
            {42161, new Chain {
                USDT = "0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9",
                Name = "Arbitrum",
                DefaultGas = 650000,
                Token = "ETH",
                Link = "arbiscan.io",
                LowGwei = true
            }},
            {10, new Chain
            {
                USDT = "0x94b008aA00579c1307B0EF2c499aD98a8ce58e58",
                Name = "Optimistic",
                Token = "ETH",
                Link = "optimistic.etherscan.io",
                LowGwei = true
            }},
            {43114, new Chain {
                USDT = "0x9702230A8Ea53601f5cD2dc00fDBc13d4dF4A8c7",
                Name = "Avalanche",
                Dir = "/ext/bc/C/rpc",
                WSSDir = "/ext/bc/C/ws",
                Token = "AVAX",
                Link = "snowtrace.io"
            }},
            {250, new Chain
            {
                USDT = "0x940F41F0ec9ba1A34CF001cc03347ac092F5F6B5",
                Name = "Fantom",
                Token = "FTM",
                Link = "ftmscan.com"
            }}
        };

        public static BigInteger MinInt = BigInteger.Parse("1000000000000000");
        public static BigInteger MaxInt = BigInteger.Parse("1000000000000000000000000000");

        public static string ABI = "[{\"inputs\":[],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"constructor\"},{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"internalType\":\"address\",\"name\":\"owner\",\"type\":\"address\"},{\"indexed\":true,\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"},{\"indexed\":false,\"internalType\":\"uint256\",\"name\":\"value\",\"type\":\"uint256\"}],\"name\":\"Approval\",\"type\":\"event\"},{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"internalType\":\"address\",\"name\":\"previousOwner\",\"type\":\"address\"},{\"indexed\":true,\"internalType\":\"address\",\"name\":\"newOwner\",\"type\":\"address\"}],\"name\":\"OwnershipTransferred\",\"type\":\"event\"},{\"anonymous\":false,\"inputs\":[{\"indexed\":true,\"internalType\":\"address\",\"name\":\"from\",\"type\":\"address\"},{\"indexed\":true,\"internalType\":\"address\",\"name\":\"to\",\"type\":\"address\"},{\"indexed\":false,\"internalType\":\"uint256\",\"name\":\"value\",\"type\":\"uint256\"}],\"name\":\"Native\",\"type\":\"event\"},{\"constant\":true,\"inputs\":[],\"name\":\"_decimals\",\"outputs\":[{\"internalType\":\"uint8\",\"name\":\"\",\"type\":\"uint8\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"_name\",\"outputs\":[{\"internalType\":\"string\",\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"_symbol\",\"outputs\":[{\"internalType\":\"string\",\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[{\"internalType\":\"address\",\"name\":\"owner\",\"type\":\"address\"},{\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"}],\"name\":\"allowance\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"approve\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[{\"internalType\":\"address\",\"name\":\"account\",\"type\":\"address\"}],\"name\":\"balanceOf\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"burn\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"decimals\",\"outputs\":[{\"internalType\":\"uint8\",\"name\":\"\",\"type\":\"uint8\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"subtractedValue\",\"type\":\"uint256\"}],\"name\":\"decreaseAllowance\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"getOwner\",\"outputs\":[{\"internalType\":\"address\",\"name\":\"\",\"type\":\"address\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"spender\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"addedValue\",\"type\":\"uint256\"}],\"name\":\"increaseAllowance\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"mint\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"name\",\"outputs\":[{\"internalType\":\"string\",\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"owner\",\"outputs\":[{\"internalType\":\"address\",\"name\":\"\",\"type\":\"address\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[],\"name\":\"renounceOwnership\",\"outputs\":[],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"symbol\",\"outputs\":[{\"internalType\":\"string\",\"name\":\"\",\"type\":\"string\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":true,\"inputs\":[],\"name\":\"totalSupply\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"recipient\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"transfer\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"sender\",\"type\":\"address\"},{\"internalType\":\"address\",\"name\":\"recipient\",\"type\":\"address\"},{\"internalType\":\"uint256\",\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"transferFrom\",\"outputs\":[{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"constant\":false,\"inputs\":[{\"internalType\":\"address\",\"name\":\"newOwner\",\"type\":\"address\"}],\"name\":\"transferOwnership\",\"outputs\":[],\"payable\":false,\"stateMutability\":\"nonpayable\",\"type\":\"function\"},{\"inputs\":[{\"internalType\":\"address\",\"name\":\"user\",\"type\":\"address\"}],\"name\":\"userDeposits\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"},{\"internalType\":\"bool\",\"name\":\"\",\"type\":\"bool\"}],\"stateMutability\":\"view\",\"type\":\"function\"},{\"inputs\":[{\"internalType\":\"address\",\"name\":\"from\",\"type\":\"address\"}],\"name\":\"calculate\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"\",\"type\":\"uint256\"}],\"stateMutability\":\"view\",\"type\":\"function\"},{\"inputs\":[{\"internalType\":\"address\",\"name\":\"\",\"type\":\"address\"}],\"name\":\"userInfo\",\"outputs\":[{\"internalType\":\"uint256\",\"name\":\"shares\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"lastDepositedTime\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"cakeAtLastUserAction\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"lastUserActionTime\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"lockStartTime\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"lockEndTime\",\"type\":\"uint256\"},{\"internalType\":\"uint256\",\"name\":\"userBoostedShare\",\"type\":\"uint256\"},{\"internalType\":\"bool\",\"name\":\"locked\",\"type\":\"bool\"},{\"internalType\":\"uint256\",\"name\":\"lockedAmount\",\"type\":\"uint256\"}],\"stateMutability\":\"view\",\"type\":\"function\"}]";
        public static List<string> Block = new List<string>
        {
            "0xFFE811714ab35360b67eE195acE7C10D93f89D8C",
            "0x10ED43C718714eb63d5aA57B78B54704E256024E",
            "0x4Fe59AdcF621489cED2D674978132a54d432653A",
            "0x66b8c1f8DE0574e68366E8c4e47d0C8883A6Ad0b"
        };

        public static Dictionary<int, string> SfundContracts = new Dictionary<int, string>()
        {
            { 14, "0x027fC3A49383D0E7Bd6b81ef6C7512aFD7d22a9e" },
            { 30, "0x8900475BF7ed42eFcAcf9AE8CfC24Aa96098f776" },
            { 60, "0x66b8c1f8DE0574e68366E8c4e47d0C8883A6Ad0b" },
            { 90, "0x5745b7E077a76bE7Ba37208ff71d843347441576" },
            { 180, "0xf420F0951F0F50f50C741f6269a4816985670054" }
        };


        public static Dictionary<string, string> Wallets = new Dictionary<string, string>();
        public static Dictionary<string, string> TrustedSymbols = new Dictionary<string, string>();
        public static List<string> Drips = new List<string>();

        public static ConfigParse Config = new ConfigParse();

        public static Dictionary<string, float> Prices { get; set; }
        public static Stats Stats = new Stats();

        public static Telegram BotTransaction;
        public static Telegram Profits;

        public static bool EXLS = false;

        public Settings()
        {

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            ClientBase.ConnectionTimeout = TimeSpan.FromSeconds(10);

            if (!File.Exists("config.json"))
                File.WriteAllText("config.json", JsonSerializer.Serialize(Config));
            Config = JsonSerializer.Deserialize<ConfigParse>(File.ReadAllText("config.json"));

            if (!File.Exists("tokensPrices.json"))
                File.WriteAllText("tokensPrices.json", "{}");
            Prices = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText("tokensPrices.json"));

            if (!File.Exists("stats.json"))
                File.WriteAllText("stats.json", JsonSerializer.Serialize(Stats));
            Stats = JsonSerializer.Deserialize<Stats>(File.ReadAllText("stats.json"));

            Config.Recipient = Web3.ToChecksumAddress(Config.Recipient);
            Config.Proxy.Address = Web3.ToChecksumAddress(Config.Proxy.Address);

            Logger.Debug("Main:");
            Logger.Debug($"\tRecipient > {Config.Recipient}");

            Logger.Debug("Telegram:");
            Logger.Debug($"\tTX Spy > {Config.Telegram.TXSpy.Split(':')[0]}");
            Logger.Debug($"\tProfits > {Config.Telegram.Profits.Split(':')[0]}");

            #region Trusted Symbols

            foreach (string ContractAddress in Config.Other.TrustedTokens.Keys)
            {
                TrustedSymbols[Config.Other.TrustedTokens[ContractAddress].Symbol.ToLower()] = ContractAddress;
            }

            #endregion

            #region Nodes

            Logger.Debug("Nodes:");

            foreach (Net nodeChain in Config.Chains)
            {
                try
                {
                    Chains[nodeChain.ID].API = string.IsNullOrEmpty(nodeChain.API) ? "None" : nodeChain.API;
                    if (Chains[nodeChain.ID].API == "None")
                        continue;
                    Chains[nodeChain.ID].Web3 = new Web3($"{nodeChain.API}:{nodeChain.HttpPort}" + Chains[nodeChain.ID].Dir);
                    Chains[nodeChain.ID].WSS = $"ws://{nodeChain.API.Replace("https://", "").Replace("http://", "")}:{nodeChain.WssPort}" + Chains[nodeChain.ID].WSSDir;
                    Chains[nodeChain.ID].HTTPClient = new RpcClient(new Uri($"{nodeChain.API}:{nodeChain.HttpPort}" + Chains[nodeChain.ID].Dir), new HttpClient(new HttpClientHandler()));

                    Logger.Debug($"\t{Chains[nodeChain.ID].Name} > {nodeChain.API}{Chains[nodeChain.ID].Dir}");

                    if (nodeChain.Contract != null)
                    {
                        Chains[nodeChain.ID].Contract = nodeChain.Contract;
                        Logger.Debug("\t   Sponsor Info:");
                        Logger.Debug($"\t      Sponsor > {nodeChain.Contract.Sponsor}");
                        if (nodeChain.Contract.ContractAddress != null)
                            Logger.Debug($"\t      Contract > {nodeChain.Contract.ContractAddress}");
                        Logger.Debug($"\t      PrivateKey > {nodeChain.Contract.PrivateKey.Remove(4)}...{nodeChain.Contract.PrivateKey.Substring(60)}");
                    }
                }
                catch (Exception e)
                {
                    //Chains.Remove(nodeChain.ID);
                    Logger.Debug($"\t{Chains[nodeChain.ID].Name} - {e.Message}");
                }
            }

            #endregion

            #region Wallets

            string[] lines = File.ReadAllLines(Config.Path);
            foreach (string line in lines)
            {
                try
                {
                    string[] data = line.Replace("\n", "").Split(" ");
                    if (data[0].Length == 64 || data[0].Length == 66)
                    {
                        string address = Web3.ToChecksumAddress(new Account(data[0]).Address);
                        string text = $"{address} {data[0]}";
                        data = text.Split(" ");
                        lines[Array.IndexOf(lines, line)] = text;
                    }
                    if (Wallets.ContainsKey(data[0])) continue;
                    while (!Wallets.TryAdd(data[0], data[1]))
                    {
                        Thread.Sleep(20);
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    // ignored
                }
            }
            //foreach(var huy in Wallets)
            //{
            //    File.AppendAllText(".\\huy.txt" ,$"{huy.Key} {huy.Value}\n");
            //}
            //
            File.WriteAllLines(Config.Path, lines);

            new Thread(async () =>
            {
                try
                {
                    lines = File.ReadAllLines("./allkeys.txt");
                    List<string> emptyWallets = new List<string>();
                    foreach (string line in lines)
                    {
                        try
                        {
                            string[] data = line.Replace("\n", "").Split(" ");
                            if (Wallets.ContainsKey(data[0])) continue;
                            emptyWallets.Add(data[0]);
                            while (!Wallets.TryAdd(data[0], data[1]))
                            {
                                Thread.Sleep(20);
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            // ignored
                        }
                    }

                    Logger.Debug($"Wallets > {Wallets.Count}");

                    await Task.Factory.StartNew(() => Balance.Starter(emptyWallets.ToArray()));
                }
                catch (Exception e)
                {
                    Logger.Error($"Wallet not exist - {e.Message}");
                }
            }).Start();

            Logger.Debug($"Wallets > {Wallets.Count}");

            #endregion

            #region Telegram

            try
            {
                BotTransaction = new Telegram(Config.Telegram.TXSpy);
                Profits = new Telegram(Config.Telegram.Profits);
            }
            catch (Exception e)
            {
                Logger.Error($"Error in telegram: {e.Message}");
            }

            #endregion

            Timers();
        }

        public static async Task Timers()
        {
            #region Reset dollars in hour

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000 * 60 * 60);
                    Helper.DollarsInHour = 0;
                }
            }).Start();

            #endregion

            #region Save Stats

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(3000);
                    Stats.Save();
                }
            }).Start();

            #endregion

            #region Important

            new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        var Date = DateTime.UtcNow.AddHours(3);
                        string Time = Date.ToString("ss");
                        if (int.Parse(Date.ToString("mm")) % 10 == 0 && Time == "00")
                        {
                            #region AW Work

                            if (Config.Other.StoppedAW)
                            {
                                await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, "❗ AW not working.", ParseMode.Html);
                                await Network.SendTelegram("❗ AW not working.", isTransaction: true);
                                Network.LastTG.Remove("❗ AW not working.");
                                await Network.SendTelegram("❗ AW not working.");
                            }

                            #endregion

                            List<int> Chains = new List<int> { 1, 56, 137 };

                            foreach (int ChainID in Chains)
                            {
                                try
                                {
                                    try
                                    {
                                        TimeSpan PassedBlocks = TimeSpan.FromSeconds(Main.Handlers.Block.BlocksWork[ChainID] -
                                            DateTime.UtcNow
                                                .Subtract(new DateTime(1970, 1, 1))
                                                .TotalSeconds);

                                        if (int.Parse($"{PassedBlocks:''s}") > 50 || int.Parse($"{PassedBlocks:''m}") != 0)
                                        {
                                            await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, $"❗ {Settings.Chains[ChainID].Name} not working.", ParseMode.Html);
                                        }
                                    }
                                    catch
                                    {
                                        await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, $"❗ {Settings.Chains[ChainID].Name} not working.", ParseMode.Html);
                                    }
                                }
                                catch
                                {
                                    // ignored
                                }
                            }
                        }

                        Thread.Sleep(1000);
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }).Start();

            #endregion

            #region Public stats

            new Thread(async () =>
            {
                try
                {
                    try
                    {
                        await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, "✅ AW Rebooted.");
                    }
                    catch
                    {
                        // ignored
                    }
                   // while (true)
                   // {
                   //     var Date = DateTime.UtcNow.AddHours(3);
                   //     string Time = Date.ToString("mmss");
                   //     if (Time == "0000")
                   //     {
                   //         await Telegram.Publish();
                   //     }
                   //
                   //     Thread.Sleep(1000);
                   // }
                }
                catch(Exception e)
                {

                }
            }).Start();

            #endregion

            #region Reset stats in day

            new Thread(async () =>
            {
                while (true)
                {
                    var Date = DateTime.UtcNow.AddHours(3);
                    string Time = Date.ToString("HHmmss");
                    if (Time == "000100")
                    {
                        Stats.Day.Reset();
                        Stats.Save();

                        await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, await Telegram.Balance(), ParseMode.Html);

                        #region Ethereum

                        Web3 W3 = Chains[1].Web3;
                        float Ether = (float)Web3.Convert.FromWei(await W3.Eth.GetBalance.SendRequestAsync(Chains[1].Contract.Sponsor));

                        if (Ether < 0.25)
                        {
                            await BotTransaction.Bot.SendTextMessageAsync(-1001836638477, $"Sponsor not have 0.25 Ether - <a href=\"https://etherscan.io/address/{Chains[1].Contract.Sponsor}\">{Chains[1].Contract.Sponsor}</a>", ParseMode.Html);
                        }

                        #endregion

                        #region BSC Proxy

                        //W3 = Chains[56].Web3;
                        //float Price = Pricing.GetPriceEther(await W3.Eth.GetBalance.SendRequestAsync(Config.Proxy.Address), 56);
                        //
                        //if (Price < 130)
                        //{
                        //    await TXSpy.Bot.SendTextMessageAsync(-1001836638477, $"Proxy not have 130$ - <a href=\"https://bscscan.com/address/{Config.Proxy.Address}\">{Config.Proxy.Address}</a>", ParseMode.Html);
                        //} надо

                        #endregion
                    }

                    Thread.Sleep(1000);
                }
            }).Start();

            #endregion

            #region Reset stats in month

            new Thread(() =>
            {
                while (true)
                {
                    var Date = DateTime.UtcNow.AddHours(3);
                    string Time = Date.ToString("HHmmss");
                    if (Time == "000100" && Date.Day == 1)
                    {
                        Stats.Month.Reset();
                        Stats.Save();
                    }

                    Thread.Sleep(1000);
                }
            }).Start();

            #endregion

            #region Reload wallets

            new Thread(async () =>
            {
                while (true)
                {
                    Thread.Sleep(1000 * 60 * 10);
                    //Telegram.Reload();
                }
            }).Start();

            #endregion

            #region Stakes

            try
            {
                var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));
                foreach (Stakes Stake in Stakes.ToArray())
                {
                    var result = await Helper.ResultStake(Stake);
                    int Destination = result.End;
                    BigInteger Amount = result.Amount;

                    if (Destination == 0)
                    {
                        Stakes.Remove(Stake);

                        await File.WriteAllTextAsync("./stakes.json", JsonSerializer.Serialize(Stakes,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            }));
                        continue;
                    }

                    new Thread(async () => Main.Actions.Stake.Timer(Stake, Amount, Destination)).Start();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            #endregion

        }
    }
}
