using System;

namespace PfSenseControl.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            /*
             * 0 - pfSense root URL ending with a slash
             * 1 - pfSense username
             * 2 - pfSense password
             */ 

            string username = args[1];
            string password = args[2];

            using (var context = new PfSenseContext(args[0], new System.Net.NetworkCredential(username, password)))
            {
                context.Login().GetAwaiter().GetResult();
                var gateways = context.GetSystemGateways().GetAwaiter().GetResult();
            }
        }
    }
}
