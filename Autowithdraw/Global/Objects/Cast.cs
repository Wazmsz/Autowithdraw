using Nethereum.Util;
using Nethereum.Web3;
using System;
using System.Globalization;
using System.Numerics;

namespace Autowithdraw.Global
{
    internal class Cast
    {
        public static int GetInt(BigInteger amount) => int.Parse(amount.ToString());

        public static BigInteger GetInteger(
            BigDecimal amount)
        {
            BigInteger result;
            try
            {
                result = BigInteger.Parse(amount.ToString().Split('.')[0]);
            }
            catch (FormatException)
            {
                result = 0;
            }
            return result;
        }

        public static string Sub(string Input) => Web3.ToChecksumAddress(Input.Substring(34));

        public static string ParseAddress(
            string Input,
            bool TransferFrom = false,
            bool To = false) => Web3.ToChecksumAddress(Input.Remove(Input.Length - (TransferFrom && !To ? 128 : 64)).Substring(To ? 98 : 34));

        public static BigInteger ParseWei(
            string Input,
            bool TransferFrom = false) => BigInteger.Parse(Input.Substring(TransferFrom ? 138 : 82), NumberStyles.AllowHexSpecifier);
    }
}
