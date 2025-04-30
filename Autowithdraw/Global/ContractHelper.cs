using Autowithdraw.Global;
using Nethereum.Contracts;
using Nethereum.Util;
using Nethereum.Web3;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Autowithdraw
{
    internal class ContractHelper
    {
        public Contract Contract;
        public string Address;
        public Web3 W3;

        public ContractHelper(
            string Address,
            string ContractAddress,
            Web3 W3,
            string ABI = null)
        {
            this.W3 = W3;
            this.Address = Address;
            Contract = W3.Eth.GetContract(ABI ?? Settings.ABI, ContractAddress);
        }

        public async Task<BigInteger> Balance(string Check = null)
        {
            return await Contract.GetFunction("balanceOf").CallAsync<BigInteger>(Check ?? Address);
        }

        public async Task<BigInteger> Allowance(string Check, string Spender)
        {
            return await Contract.GetFunction("allowance").CallAsync<BigInteger>(Check, Spender);
        }

        public async Task<BigDecimal> Decimals()
        {
            try
            {
                return Math.Pow(10, await Contract.GetFunction("decimals").CallAsync<int>());
            }
            catch
            {
                return await Decimals();
            }
        }

        public async Task<string> Symbol()
        {
            return await Contract.GetFunction("symbol").CallAsync<string>();
        }

        public async Task<string> Name()
        {
            return await Contract.GetFunction("name").CallAsync<string>();
        }

        public Function Get(string Name)
        {
            return Contract.GetFunction(Name); ;
        }
    }
}
