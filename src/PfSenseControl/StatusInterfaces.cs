using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PfSenseControl
{
    public sealed class StatusInterfaces
    {
        public enum DHCPStatuses
        {
            Unavailable,
            Unknown,
            Up,
            Down
        }

        [DebuggerDisplay("{Name} Interface ({InternalName}, {NicName})")]
        public sealed class Interface
        {
            private readonly PfSenseContext context;
            internal string dhcpReleaseFormCsrfToken = null;
            internal string dhcpReleaseFormIfDescr = null;
            internal string dhcpReleaseFormStatus = null;
            internal string dhcpReleaseFormIf = null;
            internal string dhcpReleaseFormIpV = null;


            /// <summary>
            /// User-given interface name such as WAN or LAN
            /// </summary>
            public string Name { get; internal set; }

            /// <summary>
            /// Internal pfsense interface name, such as wan, opt1, lan
            /// </summary>
            public string InternalName { get; internal set; }
            /// <summary>
            /// OS interface name, such as wlan0, or igb1 (network interface card)
            /// </summary>
            public string NicName { get; internal set; }

            /// <summary>
            /// Interface status whether it's up (true) or down (false)
            /// </summary>
            public bool Status { get; internal set; }

            /// <summary>
            /// Interface MAC address
            /// </summary>
            public string MACAddress { get; internal set; }

            public string IPv4Address { get; internal set; }

            public string SubnetMaskIPv4 { get; internal set; }

            public string GatewayIPv4 { get; internal set; }

            public string IPv6LinkLocal { get; internal set; }

            public int MTU { get; internal set; }

            public string Media { get; internal set; }

            public DHCPStatuses DHCPStatus { get; private set; }

            public IEnumerable<string> DNSServers { get; internal set; }

            private Interface(PfSenseContext context)
            {
                this.context = context;
            }

            /// <summary>
            /// Tries parsing html that contains interface description, returning the object or null if parsing fails
            /// </summary>
            /// <param name="node"></param>
            /// <returns></returns>
            public static Interface TryParse(PfSenseContext context, HtmlNode node)
            {
                var titleH2 = node.SelectSingleNode(".//h2[@class='panel-title']");
                if (titleH2 != null)
                {
                    string h2text = titleH2.InnerText.Trim();
                    var namesMatch = Regex.Match(h2text, @"(\w+) Interface \((\w+), ([\w\.]+)\)");
                    if (namesMatch.Success)
                    {
                        // interface found!
                        var iface = new Interface(context);
                        iface.parseInterfaceHtml(node, namesMatch.Groups[1].Value, namesMatch.Groups[2].Value, namesMatch.Groups[3].Value);

                        return iface;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            private void parseInterfaceHtml(HtmlNode node, string name, string internalName, string nicName)
            {
                Name = name;
                InternalName = internalName;
                NicName = nicName;

                // status is a dt with dd sibling
                Status = getDtSiblingValue(node, "Status") == "up";
                MACAddress = getDtSiblingValue(node, "MAC Address");
                IPv4Address = getDtSiblingValue(node, "IPv4 Address");
                SubnetMaskIPv4 = getDtSiblingValue(node, "Subnet mask IPv4");
                GatewayIPv4 = getDtSiblingValue(node, "Gateway IPv4");
                IPv6LinkLocal = getDtSiblingValue(node, "IPv6 Link Local");
                MTU = int.Parse(getDtSiblingValue(node, "MTU") ?? "-1");
                Media = getDtSiblingValue(node, "Media");

                // special handling for DHCP interfaces
                parseDHCPStatus(node);

                // special handling for DNS servers
                DNSServers = getDnsServers(node);

                // TODO special handling for packets stats
            }

            private void parseDHCPFormContent(HtmlNode dhcpForm)
            {
                // required
                var csrfMagic = dhcpForm.SelectSingleNode(".//input[@name='__csrf_magic']");
                var ifDescr = dhcpForm.SelectSingleNode(".//input[@name='ifdescr']");
                var ifStatus = dhcpForm.SelectSingleNode(".//input[@name='status']");

                // optional
                var iface = dhcpForm.SelectSingleNode(".//input[@name='if']");
                var ipv = dhcpForm.SelectSingleNode(".//input[@name='ipv']");
                if (csrfMagic != null && ifDescr != null && ifStatus != null)
                {
                    switch (ifStatus.Attributes["value"].Value)
                    {
                        case "up":
                            DHCPStatus = DHCPStatuses.Up;
                            break;
                        case "down":
                            DHCPStatus = DHCPStatuses.Down;
                            break;
                        default:
                            DHCPStatus = DHCPStatuses.Unknown; // hopefully never happens
                            break;
                    }

                    // save form params for later
                    dhcpReleaseFormCsrfToken = csrfMagic.Attributes["value"].Value;
                    dhcpReleaseFormIfDescr = ifDescr.Attributes["value"].Value;
                    dhcpReleaseFormStatus = ifStatus.Attributes["value"].Value;
                    dhcpReleaseFormIf = iface?.Attributes["value"].Value;
                    dhcpReleaseFormIpV = ipv?.Attributes["value"].Value;
                }
                else
                {
                    throw new InvalidOperationException($"DHCP Status and Release form exist for interface {this.Name}, but some of the hidden fields could not be parsed, this is a bug");
                }
            }

            private void parseDHCPStatus(HtmlNode node)
            {
                var dt = node.SelectSingleNode(".//dt[contains(text(),'DHCP')]");
                if (dt != null && dt.NextSibling?.Name == "dd")
                {
                    // DHCP dd also contains a form to Release/Renew/Relinquish lease
                    var dhcpForm = dt.NextSibling.SelectSingleNode(".//form");
                    if (dhcpForm != null)
                    {
                        parseDHCPFormContent(dhcpForm);
                    }
                    else
                    {
                        throw new InvalidOperationException($"DHCP Status exists for interface {this.Name}, but release form not found, this is a bug");
                    }
                }
                else
                {
                    DHCPStatus = DHCPStatuses.Unknown; // DHCP not configured?
                }
            }

            private static IEnumerable<string> getDnsServers(HtmlNode node)
            {
                // first dt
                var dtDns = node.SelectSingleNode(".//dt[contains(text(),'DNS servers')]");

                // setup first DNS server entry
                var ddDns = dtDns?.NextSibling?.Name == "dd" ? dtDns.NextSibling : null;

                if (ddDns != null)
                {
                    do
                    {
                        var dnsValue = ddDns.InnerText.Trim();
                        yield return dnsValue;
                        if (
                            ddDns.NextSibling?.Name == "dt" &&
                            ddDns.NextSibling?.InnerText?.Trim() == string.Empty && // continue as long as we have empty dt to follow...
                            ddDns.NextSibling?.NextSibling?.Name == "dd" // ... and its next sibling is also a dd
                            )
                        {
                            ddDns = ddDns.NextSibling.NextSibling;
                        }
                        else
                        {
                            ddDns = null;
                        }
                    } while (ddDns != null);
                }
                else
                {
                    yield break; // no DNS entries for this interface
                }
            }

            private static string getDtSiblingValue(HtmlNode node, string dtText)
            {
                var dt = node.SelectSingleNode($".//dt[contains(text(),'{dtText}')]");
                if (dt != null && dt.NextSibling?.Name == "dd")
                {
                    return dt.NextSibling.InnerText.Trim();
                }
                else
                {
                    return null;
                }
            }
        }



        private List<Interface> interfaces = new List<Interface>();
        readonly PfSenseContext context;

        public IEnumerable<Interface> Interfaces
        {
            get
            {
                return interfaces.AsReadOnly();
            }
        }

        internal StatusInterfaces(PfSenseContext context, string htmlBody)
        {
            this.context = context;
            parsePageContent(htmlBody);
        }

        internal static async Task<string> GetStatusInterfacesContent(PfSenseContext context)
        {
            var request = context.CreateHttpRequestMessage(HttpMethod.Get, "status_interfaces.php");
            var response = await context.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        private void parsePageContent(string htmlBody)
        {
            var parser = new HtmlDocument();
            parser.LoadHtml(htmlBody);

            interfaces?.Clear();
            interfaces = null;
            interfaces = new List<Interface>();

            // each interface seems to be in a <div class="panel panel-default"> container
            var interfaceContainers = parser.DocumentNode.SelectNodes("//div[@class='panel panel-default']");
            foreach (var interfaceDiv in interfaceContainers)
            {
                var iface = Interface.TryParse(context, interfaceDiv);
                if (iface != null)
                {
                    interfaces.Add(iface);
                }
            }
        }

        /// <summary>
        /// If interface is configured with DHCP, perform a DHCP Release
        /// </summary>
        /// <param name="relinquishLease">Send gratuitous DHCP release message to server</param>
        /// <remarks>https://github.com/pfsense/pfsense/commit/718432f1a12b828214413881d4b9d612c2ef3c09</remarks>
        public async Task DHCPRelease(Interface netInterface, bool relinquishLease = false)
        {
            if (netInterface == null)
                throw new ArgumentNullException(nameof(netInterface));
            if (interfaces.Contains(netInterface))
            {
                if (netInterface.DHCPStatus == DHCPStatuses.Up)
                {
                    if (
                        netInterface.dhcpReleaseFormCsrfToken != null &&
                        netInterface.dhcpReleaseFormIfDescr != null &&
                        netInterface.dhcpReleaseFormStatus != null &&
                        netInterface.dhcpReleaseFormIf != null &&
                        netInterface.dhcpReleaseFormIpV != null
                       )
                    {
                        var request = context.CreateHttpRequestMessage(HttpMethod.Post, "status_interfaces.php");
                        var formParams = new Dictionary<string, string>()
                        {
                            {"__csrf_magic", netInterface.dhcpReleaseFormCsrfToken },
                            {"ifdescr", netInterface.dhcpReleaseFormIfDescr },
                            {"status", netInterface.dhcpReleaseFormStatus },
                            {"submit", "Release" },
                            {"relinquish_lease", relinquishLease ? "true" : "false" }
                        };

                        // add optional fields - available only when interface can "Release"
                        if (netInterface.dhcpReleaseFormIf != null)
                            formParams.Add("if", netInterface.dhcpReleaseFormIf);
                        if (netInterface.dhcpReleaseFormIpV != null)
                            formParams.Add("ipv", netInterface.dhcpReleaseFormIpV);

                        request.Content = new FormUrlEncodedContent(formParams);
                        var response = await context.SendAsync(request, true); // expect 302
                        if (response.StatusCode == HttpStatusCode.Found)
                        {
                            // follow-through with reloading content
                            if (
                                response.Headers.Location != null &&
                                response.Headers.Location.OriginalString == "status_interfaces.php"
                                )
                            {
                                // re-request fresh page
                                parsePageContent(await GetStatusInterfacesContent(context));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Expected pfSense to 302 redirect me to status_interfaces.php but that did not happen, this is a bug");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Missing required form parameters for DHCP Release form, this is a bug");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Interface is not a DHCP Interface or its status is not Up");
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown interface provided, the Interface object must come from the Interfaces list of this class instance");
            }
        }

        /// <summary>
        /// If interface is configured with DHCP, causes a renew request to be sent to obtain a lease
        /// </summary>
        /// <param name="netInterface"></param>
        /// <returns></returns>
        public async Task DHCPRenew(Interface netInterface)
        {
            if (netInterface == null)
                throw new ArgumentNullException(nameof(netInterface));
            if (interfaces.Contains(netInterface))
            {
                if (netInterface.DHCPStatus == DHCPStatuses.Down)
                {
                    if (
                        netInterface.dhcpReleaseFormCsrfToken != null &&
                        netInterface.dhcpReleaseFormIfDescr != null &&
                        netInterface.dhcpReleaseFormStatus != null
                       )
                    {
                        var request = context.CreateHttpRequestMessage(HttpMethod.Post, "status_interfaces.php");
                        var formParams = new Dictionary<string, string>()
                        {
                            {"__csrf_magic", netInterface.dhcpReleaseFormCsrfToken },
                            {"ifdescr", netInterface.dhcpReleaseFormIfDescr },
                            {"status", netInterface.dhcpReleaseFormStatus },
                            {"submit", "Renew" }
                        };

                        request.Content = new FormUrlEncodedContent(formParams);
                        var response = await context.SendAsync(request);

                        // parse new result 
                        parsePageContent(await response.Content.ReadAsStringAsync());
                    }
                    else
                    {
                        throw new InvalidOperationException("Missing required form parameters for DHCP Renew form, this is a bug");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Interface is not a DHCP Interface or its status is not Down");
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown interface provided, the Interface object must come from the Interfaces list of this class instance");

            }


        }
    }
}
