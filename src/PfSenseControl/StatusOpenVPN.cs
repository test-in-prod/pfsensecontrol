using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PfSenseControl
{
    public sealed class StatusOpenVPN
    {
        private readonly PfSenseContext context;
        private readonly List<ClientInstance> clients = new List<ClientInstance>();

        public enum TunnelStatuses
        {
            Unknown,
            Up,
            Down
        }

        public sealed class ClientInstance
        {
            public string Name { get; private set; }
            public TunnelStatuses TunnelStatus { get; private set; }
            public DateTime? ConnectedTime { get; private set; }
            public string LocalAddress { get; private set; }
            public string VirtualAddress { get; private set; }
            public string RemoteHost { get; private set; }
            public long BytesSent { get; private set; }
            public long BytesReceived { get; private set; }

            internal static ClientInstance TryParse(HtmlNode[] cells)
            {
                if (cells.Length == 8) // should only be this many cells
                {
                    var name = cells[0].InnerText.Trim();
                    var status = cells[1].InnerText.Trim();
                    var connectedSince = cells[2].InnerText.Trim();
                    var localAddress = cells[3].InnerText.Trim();
                    var virtualAddress = cells[4].InnerText.Trim();
                    var remoteHost = cells[5].InnerText.Trim();
                    var txrxStats = cells[6].InnerText.Trim();

                    var instance = new ClientInstance();

                    instance.Name = name;
                    instance.TunnelStatus = parseTunnelStatus(status);
                    instance.ConnectedTime = parseConnectedSince(connectedSince);
                    instance.LocalAddress = parseLocalAddress(localAddress);
                    instance.VirtualAddress = parseVirtualAddress(virtualAddress);
                    instance.RemoteHost = parseRemoteHost(remoteHost);

                    if (tryParseTxRxBytes(txrxStats, out long rxBytes, out long txBytes))
                    {
                        instance.BytesReceived = rxBytes;
                        instance.BytesSent = txBytes;
                    }

                    return instance;
                }
                else
                {
                    return null;
                }
            }

            private static bool tryConvertIECUnitsToBytes(string uom, decimal value, out long result)
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

            private static bool tryParseTxRxBytes(string txrxStats, out long rxBytes, out long txBytes)
            {
                // example of possible values like as "Sent/Received"
                // 2.98 GiB / 20.27 GiB
                // 0 B / 0 B
                // pfsense uses IEC units so we'll calculate down to bytes as needed
                var regex = Regex.Match(txrxStats, @"(\d+(?:\.\d+)?) (\w+) \/ (\d+(?:\.\d+)?) (\w+)");
                if (regex.Success)
                {
                    var txuom = regex.Groups[2].Value;
                    var rxuom = regex.Groups[4].Value;
                    var txvalue = decimal.Parse(regex.Groups[1].Value);
                    var rxvalue = decimal.Parse(regex.Groups[3].Value);

                    if (
                        tryConvertIECUnitsToBytes(txuom, txvalue, out txBytes) &&
                        tryConvertIECUnitsToBytes(rxuom, rxvalue, out rxBytes)
                        )
                    {
                        return true;
                    }
                    else
                    {
                        txBytes = -1;
                        rxBytes = -1;
                        return false;
                    }
                }
                else
                {
                    rxBytes = -1;
                    txBytes = -1;
                    return false;
                }
            }

            private static string parseRemoteHost(string remoteHost)
            {
                // TODO make IPv6 compatible (oh dear...)
                return Regex.IsMatch(remoteHost, @"\d+\.\d+\.\d+\.\d+:\d+") ? remoteHost : null;
            }

            private static string parseVirtualAddress(string virtualAddress)
            {
                // pfsense puts up random messages instead of an IP if interface is down, for instance "Service not running?"
                // TODO make IPv6 compatible
                return Regex.IsMatch(virtualAddress, @"\d+\.\d+\.\d+\.\d+") ? virtualAddress : null;
            }

            private static string parseLocalAddress(string localAddress)
            {
                // pfsense puts up random messages instead of an IP:PORT if interface is down, for instance "(pending)"
                // which isn't terribly useful
                // simple-enough regex to make sure we got something like IP:PORT in here
                // TODO make IPv6 compatible (oh dear...)
                return Regex.IsMatch(localAddress, @"\d+\.\d+\.\d+\.\d+:\d+") ? localAddress : null;
            }

            private static TunnelStatuses parseTunnelStatus(string tunnelStatus)
            {
                switch (tunnelStatus)
                {
                    case "up":
                        return TunnelStatuses.Up;
                    case "down":
                        return TunnelStatuses.Down;
                    default:
                        return TunnelStatuses.Unknown;
                }
            }

            private static DateTime? parseConnectedSince(string connectedSince)
            {
                if (!string.IsNullOrEmpty(connectedSince) &&
                        DateTime.TryParseExact(connectedSince, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime connectedSinceDt))
                {
                    return connectedSinceDt;
                }
                else
                {
                    return null;
                }
            }

        }

        public IEnumerable<ClientInstance> Clients
        {
            get
            {
                return clients.AsReadOnly();
            }
        }

        internal StatusOpenVPN(PfSenseContext context, string htmlContent)
        {
            this.context = context;
            parseHtmlContent(htmlContent);
        }

        private void parseHtmlContent(string htmlContent)
        {
            var html = new HtmlDocument();
            html.LoadHtml(htmlContent);

            parseClientStats(html);

        }

        private void parseClientStats(HtmlDocument html)
        {
            clients.Clear();

            // find a panel div with this title and then navigate to a sibling that has the actual status
            var panelTitle = html.DocumentNode.SelectSingleNode("//h2[contains(text(),'Client Instance Statistics')]");
            if (panelTitle != null)
            {
                var panel = panelTitle
                    .ParentNode // <div class="panel-heading">
                    .ParentNode; // <div class="panel panel-default"> <-- this thing

                // find the table (there should only be one like this)
                var table = panel.SelectNodes(".//table").FirstOrDefault(n => n.Attributes["class"].Value.Contains("table table-striped"));
                if (table != null)
                {
                    foreach (var tnode in table.ChildNodes)
                    {
                        if (tnode.Name == "tbody")
                        {
                            var rows = tnode.SelectNodes(".//tr");
                            foreach (var row in rows)
                            {
                                parseClientStatsRow(row);
                            }
                            break;
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException("OpenVPN Client Instance Statistics table could not be found, this is a bug");
                }
            }
            else
            {
                throw new InvalidOperationException("OpenVPN Client Instance Statistics panel div could not be found, this is a bug");
            }
        }

        private void parseClientStatsRow(HtmlNode row)
        {
            var tdOnly = row.SelectNodes("./td").ToArray();
            var client = ClientInstance.TryParse(tdOnly);
            if (client != null)
            {
                clients.Add(client);
            }
        }

        internal static async Task<string> GetStatusOpenVPNContentAsync(PfSenseContext context)
        {
            var request = context.CreateHttpRequestMessage(HttpMethod.Get, "status_openvpn.php");
            var response = await context.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }




    }
}
