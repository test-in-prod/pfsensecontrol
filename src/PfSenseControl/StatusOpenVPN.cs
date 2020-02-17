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
        private readonly List<ClientInstance> clientInstances = new List<ClientInstance>();
        private readonly List<Tuple<string, ClientConnection>> clientConnections = new List<Tuple<string, ClientConnection>>();

        private string csrfMagicToken = null;

        #region Client Instance Statistics
        public enum TunnelStatuses
        {
            Unknown,
            Up,
            Down
        }

        public enum ServiceStatuses
        {
            Unknown,
            Running,
            Stopped
        }

        public sealed class ClientInstance
        {
            internal string ID { get; set; }

            public string Name { get; private set; }
            public TunnelStatuses TunnelStatus { get; private set; }
            public DateTime? ConnectedTime { get; private set; }
            public string LocalAddress { get; private set; }
            public string VirtualAddress { get; private set; }
            public string RemoteHost { get; private set; }
            public long BytesSent { get; private set; }
            public long BytesReceived { get; private set; }

            public ServiceStatuses ServiceStatus { get; private set; }

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

                    instance.ServiceStatus = parseServiceStatus(cells[7]);

                    return instance;
                }
                else
                {
                    return null;
                }
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
                        Utilities.TryConvertIECUnitsToBytes(txuom, txvalue, out txBytes) &&
                        Utilities.TryConvertIECUnitsToBytes(rxuom, rxvalue, out rxBytes)
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

            private static ServiceStatuses parseServiceStatus(HtmlNode node)
            {
                // find <i> and determine what icon it is basically
                var iElements = node.SelectNodes(".//i");
                foreach (var i in iElements)
                {
                    var titleText = i.Attributes["title"]?.Value ?? string.Empty;
                    if (titleText.Contains("openvpn Service is Stopped"))
                    {
                        return ServiceStatuses.Stopped;
                    }
                    else
                        if (titleText.Contains("openvpn Service is Running"))
                    {
                        return ServiceStatuses.Running;
                    }
                }
                return ServiceStatuses.Unknown;
            }
        }
        #endregion
        public sealed class ClientConnection
        {
            public string CommonName { get; private set; }

            public string RealAddress { get; private set; }

            public string VirtualAddress { get; private set; }

            public DateTime? ConnectedTime { get; private set; }

            public long BytesSent { get; private set; }

            public long BytesReceived { get; private set; }

            internal static ClientConnection TryParse(HtmlNode[] cells)
            {
                if (cells.Length == 6)
                {
                    var commonName = cells[0].InnerText.Trim();
                    var realAddress = cells[1].InnerText.Trim();
                    var virtualAddress = cells[2].InnerText.Trim();
                    var connectedSince = cells[3].InnerText.Trim();
                    var bytesTxRx = cells[4].InnerText.Trim();

                    var conn = new ClientConnection();
                    conn.CommonName = commonName;
                    conn.RealAddress = parseRealAddress(realAddress);
                    conn.VirtualAddress = parseVirtualAddress(virtualAddress);
                    conn.ConnectedTime = parseConnectedSince(connectedSince);

                    if (tryParseTxRxBytes(bytesTxRx, out long rxBytes, out long txBytes))
                    {
                        conn.BytesReceived = rxBytes;
                        conn.BytesSent = txBytes;
                    }
                    return conn;
                }
                else
                {
                    return null;
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

            private static string parseVirtualAddress(string virtualAddress)
            {
                return Regex.IsMatch(virtualAddress, @"\d+\.\d+\.\d+\.\d+") ? virtualAddress : null;
            }

            private static string parseRealAddress(string realAddress)
            {
                return Regex.IsMatch(realAddress, @"\d+\.\d+\.\d+\.\d+:\d+") ? realAddress : null;
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
                        Utilities.TryConvertIECUnitsToBytes(txuom, txvalue, out txBytes) &&
                        Utilities.TryConvertIECUnitsToBytes(rxuom, rxvalue, out rxBytes)
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
        }

        public IEnumerable<Tuple<string, ClientConnection>> Clients
        {
            get { return clientConnections.AsReadOnly(); }
        }

        public IEnumerable<ClientInstance> ClientInstances
        {
            get
            {
                return clientInstances.AsReadOnly();
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

            findCsrfMagicToken(html);
            parseClientConnections(html);
            parseClientStats(html);
        }

        private void parseClientConnections(HtmlDocument html)
        {
            clientConnections.Clear();

            // find a panel div with a title that contains vpn server name with port combinations + "Client Connections" at the end
            // there may be more than one server!
            var panelTitles = html.DocumentNode.SelectNodes("//h2[contains(text(),'Client Connections')]");
            foreach (var panelTitle in panelTitles)
            {
                parseClientConnectionsByPanelTitle(panelTitle);
            }
        }

        private void parseClientConnectionsByPanelTitle(HtmlNode panelTitle)
        {
            // make sure title matches regex
            var titleMatch = Regex.Match(panelTitle.InnerText.Trim(), @"(.+) UDP|TCP.+ Client Connections");
            // TODO WARN! possible bug, maybe there's a better way to check the title?
            if (titleMatch.Success)
            {
                var serverTitle = titleMatch.Groups[1].Value;
                var panel = panelTitle
                    .ParentNode // <div class="panel-heading">
                    .ParentNode; // <div class="panel panel-default"> <-- this thing

                // find the table.
                var table = panel.SelectNodes(".//table").FirstOrDefault(n => n.Attributes["class"].Value.Contains("table table-striped"));
                if (table != null)
                {
                    foreach (var tnode in table.ChildNodes)
                    {
                        if (tnode.Name == "tbody")
                        {
                            var rows = tnode.SelectNodes(".//tr");
                            if (rows != null)
                            {
                                foreach (var row in rows)
                                {
                                    parseClientConnectionRow(serverTitle, row);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void parseClientConnectionRow(string serverTitle, HtmlNode row)
        {
            var tdOnly = row.SelectNodes("./td").ToArray();
            var client = ClientConnection.TryParse(tdOnly);
            if (client != null)
            {
                var idStuff = Regex.Match(row.Attributes["id"].Value, @"r:(\w+):(.+):(\d+)");
                clientConnections.Add(new Tuple<string, ClientConnection>(serverTitle, client));
            }
        }

        private void findCsrfMagicToken(HtmlDocument html)
        {
            // this page has a <script> tag in the <head> that contains javascript 
            // like var csrfMagicToken ...  we can't execute js, so we'll just have to find it
            var head = html.DocumentNode.SelectSingleNode("//head");
            if (head != null)
            {
                csrfMagicToken = null; // default
                var regex = new Regex(@"(sid:[\w\d,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var scripts = head.SelectNodes(".//script");
                foreach (var script in scripts)
                {
                    var match = regex.Match(script.InnerText.Trim());
                    if (match.Success)
                    {
                        csrfMagicToken = match.Groups[1].Value;
                        break;
                    }
                }
            }
        }

        #region Client Instance Statistics - table parsing
        private void parseClientStats(HtmlDocument html)
        {
            clientInstances.Clear();

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
                            if (rows != null)
                            {
                                foreach (var row in rows)
                                {
                                    parseClientStatsRow(row);
                                }
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
                var idMatch = Regex.Match(row.Attributes["id"].Value, @"r::(\d+)");
                client.ID = idMatch.Groups[1].Value;
                clientInstances.Add(client);
            }
        }
        #endregion

        internal static async Task<string> GetStatusOpenVPNContentAsync(PfSenseContext context)
        {
            var request = context.CreateHttpRequestMessage(HttpMethod.Get, "status_openvpn.php");
            var response = await context.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Refreshes the view from pfSense on latest clients
        /// </summary>
        /// <returns></returns>
        public async Task Refresh()
        {
            parseHtmlContent(await GetStatusOpenVPNContentAsync(context));
        }

        /// <summary>
        /// Restarts a given OpenVPN client service (connection)
        /// </summary>
        /// <param name="instance"></param>
        /// <returns>Refreshed instance that matches the ClientInstance given</returns>
        public async Task<ClientInstance> RestartClientService(ClientInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            if (csrfMagicToken == null)
                throw new InvalidOperationException("CSRF Token was not found/parsed, this is a bug");
            if (clientInstances.Contains(instance))
            {
                var request = context.CreateHttpRequestMessage(HttpMethod.Post, "status_services.php");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"__csrf_magic", csrfMagicToken },
                    {"ajax", "ajax" },
                    {"mode", "restartservice" },
                    {"service", "openvpn" },
                    {"vpnmode", "client" },
                    {"zone", "client" },
                    {"id", instance.ID }
                });
                await context.SendAsync(request); // no need to check response content (there's none)
                await Refresh();
                return clientInstances.FirstOrDefault(c => c.ID == instance.ID);
            }
            else
            {
                throw new InvalidOperationException("Given instance is not part of known client connections, try refreshing list via call to GetStatusOpenVPN again");
            }
        }


    }
}
