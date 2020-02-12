using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PfSenseControl
{
    public sealed class SystemGateways
    {
        List<GatewayTableListItem> gateways = null;
        readonly PfSenseContext context;

        private string formSaveDefaultGatewaysCsrfToken = null;
        private string formApplyChangesCsrfToken = null;

        /// <summary>
        /// Gets the gateways table
        /// </summary>
        public IEnumerable<GatewayTableListItem> Gateways
        {
            get
            {
                return gateways.AsReadOnly();
            }
        }

        /// <summary>
        /// Select the gateway or gatewaygroup to use as the default gateway.
        /// </summary>
        public DefaultGatewayList DefaultGatewayIPv4 { get; private set; }
        /// <summary>
        /// Select the gateway or gatewaygroup to use as the default gateway.
        /// </summary>
        public DefaultGatewayList DefaultGatewayIPv6 { get; private set; }

        internal SystemGateways(PfSenseContext context, string htmlBody)
        {
            this.context = context;
            parsePageContent(htmlBody);
        }

        private void parsePageContent(string htmlBody)
        {
            var parser = new HtmlDocument();
            parser.LoadHtml(htmlBody);

            gateways = parseGatewaysTable(parser);

            DefaultGatewayIPv4 = parseList(parser, "defaultgw4");
            DefaultGatewayIPv6 = parseList(parser, "defaultgw6");

            formSaveDefaultGatewaysCsrfToken = extractDefaultGatewayFormCsrfToken(parser);

            // "changes must be applied" banner that shows if Gateways have been saved
            formApplyChangesCsrfToken = extractApplyChangesFormCsrfToken(parser);
        }

        public async Task SaveDefaultGateways(bool applyChanges = false)
        {
            var request = context.CreateHttpRequestMessage(HttpMethod.Post, "system_gateways.php");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"__csrf_magic", formSaveDefaultGatewaysCsrfToken },
                {"defaultgw4", DefaultGatewayIPv4.Selected.InternalName },
                {"defaultgw6", DefaultGatewayIPv6.Selected.InternalName },
                {"save", "Save" }
            });
            var response = await context.SendAsync(request);
            parsePageContent(await response.Content.ReadAsStringAsync());

            if (applyChanges)
            {
                await ApplyChanges();
            }
        }

        public async Task ApplyChanges()
        {
            if (formApplyChangesCsrfToken != null)
            {
                var request = context.CreateHttpRequestMessage(HttpMethod.Post, "system_gateways.php");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"__csrf_magic", formApplyChangesCsrfToken },
                    {"apply", "Apply Changes" }
                });
                var response = await context.SendAsync(request);
                parsePageContent(await response.Content.ReadAsStringAsync());
            } // else do nothing since there's not a token
        }

        private static string extractApplyChangesFormCsrfToken(HtmlDocument parser)
        {
            // form is not easily identifiable, let's find a button that contains "Apply Changes" instead
            var applyChangesBtn = parser.DocumentNode.SelectSingleNode("//button[@name='apply']");
            if (applyChangesBtn != null)
            {
                var form = applyChangesBtn.ParentNode;
                var csrfField = form.SelectSingleNode("input[@name='__csrf_magic']");
                if (csrfField == null)
                    throw new InvalidOperationException("Apply Changes form found, but csrf hidden field was not, this is a bug");
                return csrfField.Attributes["value"].Value;
            }
            else
            {
                // form does not exist, so there's no csrf token to speak of
                return null;
            }
        }

        private static string extractDefaultGatewayFormCsrfToken(HtmlDocument parser)
        {
            // form to save default gateways has a csrf token
            var formSaveDefaultGw = parser.DocumentNode.SelectSingleNode("//form[@action='/system_gateways.php']");
            if (formSaveDefaultGw == null)
                throw new InvalidOperationException("Unable to locate form to save default gateway config, this is a bug");
            var formSaveDefaultGwCsrf = formSaveDefaultGw.SelectSingleNode("input[@name='__csrf_magic']");
            if (formSaveDefaultGwCsrf == null)
                throw new InvalidOperationException("Unable to locate csrf magic hidden field in the save default gateway form, this is a bug");
            return formSaveDefaultGwCsrf.Attributes["value"].Value;
        }

        private static List<GatewayTableListItem> parseGatewaysTable(HtmlDocument parser)
        {
            var list = new List<GatewayTableListItem>();

            var gatewaysTable = parser.DocumentNode.SelectSingleNode("//table[@id='gateways']");
            if (gatewaysTable == null)
                throw new InvalidOperationException("Gateways table was not found, this is a bug");

            var tbody = gatewaysTable.ChildNodes.FindFirst("tbody");
            if (tbody == null)
                throw new InvalidOperationException("Gateways table does not contain a tbody element, this is a bug");

            foreach (var tr in tbody.ChildNodes.Where(n => n.Name == "tr"))
            {
                var item = new GatewayTableListItem(tr);
                list.Add(item);
            }
            return list;
        }

        private static DefaultGatewayList parseList(HtmlDocument parser, string fieldName)
        {
            var select = parser.DocumentNode.SelectSingleNode($"//select[@name='{fieldName}']");
            if (select == null)
                throw new InvalidOperationException($"Unable to parse html, cannot find select element named {fieldName}, this is a bug");

            var list = new DefaultGatewayList();
            foreach (var opt in select.ChildNodes)
            {
                if (opt.Name == "option")
                {
                    var item = new DefaultGatewayListItem() { InternalName = opt.Attributes["value"].Value, Name = opt.InnerText };
                    list.items.Add(item);
                    if (opt.Attributes["selected"] != null)
                    {
                        list.Selected = item;
                    }
                }
            }
            return list;
        }

    }
}
