using System;
using System.Collections.Generic;
using System.Text;

namespace PfSenseControl
{
    internal static class Utilities
    {

        public static bool TryConvertIECUnitsToBytes(string uom, decimal value, out long result)
        {
            switch (uom)
            {
                case "B": // as-is
                    result = (long)Math.Round(value);
                    break;
                case "KiB": // kibibytes
                    result = (long)Math.Round(value * 1024m);
                    break;
                case "MiB": // mebibytes
                    result = (long)Math.Round(value * 1024m * 1024m);
                    break;
                case "GiB": // gibibytes
                    result = (long)Math.Round(value * 1024m * 1024m * 1024m);
                    break;
                case "TiB": // tebibyte
                    result = (long)Math.Round(value * 1024m * 1024m * 1024m * 1024m);
                    break;
                // TODO I'll be very impressed if traffic stats get to a pebibyte
                default:
                    result = -1;
                    return false;
            }
            return true;
        }

    }
}
