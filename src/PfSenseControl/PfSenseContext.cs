using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PfSenseControl
{
    /// <summary>
    /// Provides methods for controlling pfSense
    /// </summary>
    public sealed class PfSenseContext : IDisposable
    {
        private const string userAgent = "PfSenseContext/.NET";

        private SecureString sessionId = null;

        private readonly NetworkCredential loginCredential = null;
        private readonly string rootUrl = null;

        private readonly HttpClient httpClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false }, true);

        /// <summary>
        /// Creates a new pfSense context using provided root URL and credentials
        /// </summary>
        /// <param name="pfSenseRootUrl">Root URL of pfSense appliance</param>
        /// <param name="credential">pfSense credentials</param>
        public PfSenseContext(string pfSenseRootUrl, NetworkCredential credential)
        {
            rootUrl = pfSenseRootUrl;
            loginCredential = credential;
            httpClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false }, true);
            setupDefaultHeaders();
        }

        /// <summary>
        /// Creates a new pfSense context using provided root URL and credentials and custom server certificate validation callback
        /// </summary>
        /// <param name="pfSenseRootUrl">Root URL of pfSense appliance</param>
        /// <param name="credential">pfSense credentials</param>
        /// <param name="serverCertificateValidationCallback">Function that will check pfSense server certificate to validate TLS connection</param>
        public PfSenseContext(string pfSenseRootUrl, NetworkCredential credential,
            Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> serverCertificateValidationCallback)
            : this(pfSenseRootUrl, credential)
        {
            httpClient?.Dispose();
            httpClient = new HttpClient(new HttpClientHandler()
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = serverCertificateValidationCallback
            }, true);
            setupDefaultHeaders();
        }

        private void setupDefaultHeaders()
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            httpClient.DefaultRequestHeaders.Add("Accept", "text/html");
        }

        private void checkSessionIdLoggedIn()
        {
            if (sessionId == null)
            {
                throw new InvalidOperationException("Not logged in. Call Login() method first to establish session");
            }
        }

        private void checkDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(PfSenseContext));
        }

        private void updateSessionCookie(HttpResponseHeaders headers)
        {
            var setCookieHeaderValue = headers.GetValues("Set-Cookie").FirstOrDefault();
            if (setCookieHeaderValue != null)
            {
                var match = Regex.Match(setCookieHeaderValue, @"(\w+)=(\w+);");
                if (match.Success && match.Groups[1].Value == "PHPSESSID")
                {
                    var newSessionId = match.Groups[2].Value;

                    sessionId?.Dispose();
                    sessionId = new SecureString();
                    foreach (var c in newSessionId)
                    {
                        sessionId.AppendChar(c);
                    }
                    sessionId.MakeReadOnly();
                    newSessionId = null;
                    match = null;
                    setCookieHeaderValue = null;

                    GC.Collect();
                }
            }
            else
            {
                throw new InvalidOperationException("No Set-Cookie header found in previous response");
            }
        }

        private void setSessionHeaders(HttpRequestHeaders headers)
        {
            if (headers.Contains("Cookie"))
            {
                headers.Remove("Cookie");
            }
            headers.Add("Cookie", $"PHPSESSID={secureStringToString(sessionId)}");
        }

        /// <summary>
        /// Creates a new HttpRequestMessage containing required headers
        /// </summary>
        /// <param name="httpMethod"></param>
        /// <param name="relativePath">Relative path, such as "system_gateways.php"</param>
        /// <returns></returns>
        internal HttpRequestMessage CreateHttpRequestMessage(HttpMethod httpMethod, string relativePath)
        {
            var msg = new HttpRequestMessage(httpMethod, $"{rootUrl}{relativePath}");
            setSessionHeaders(msg.Headers);
            return msg;
        }

        internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, bool suppressEnsureSuccessStatusCode = false)
        {
            var response = await httpClient.SendAsync(message);
            if (!suppressEnsureSuccessStatusCode)
                response = response.EnsureSuccessStatusCode();
            updateSessionCookie(response.Headers);
            return response;
        }

        /// <summary>
        /// Attempts to establish pfSense login session
        /// </summary>
        public async Task Login()
        {
            checkDisposed();

            // get login page
            var getRootRequest = await httpClient.GetAsync(rootUrl);
            var getRootResponse = getRootRequest.EnsureSuccessStatusCode();
            updateSessionCookie(getRootResponse.Headers);

            // parse page
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(await getRootResponse.Content.ReadAsStringAsync());

            // find login form
            var loginFormElement = htmlDoc.DocumentNode.SelectSingleNode("//form[@class='login']");
            if (loginFormElement == null)
            {
                if (sessionId == null)
                {
                    // TODO session not established and login form cannot be parsed
                    throw new InvalidOperationException("Unable to parse login page HTML, this is potentially a bug");
                }
                else
                {
                    return; // we're logged in already
                }
            }
            else
            {
                // csrf token hidden field
                var csrfHidden = loginFormElement.SelectSingleNode("//input[@name='__csrf_magic']");
                if (csrfHidden == null)
                    throw new InvalidOperationException("Unable to locate __csrf_magic hidden field, this is potentially a bug");

                var csrfToken = csrfHidden.Attributes["value"].Value;

                // form params
                var loginParams = new Dictionary<string, string>
                {
                    {"__csrf_magic", csrfToken },
                    {"usernamefld", loginCredential.UserName },
                    {"passwordfld", loginCredential.Password },
                    {"login", "Sign In" }
                };

                var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{rootUrl}index.php");
                setSessionHeaders(loginRequest.Headers);
                loginRequest.Content = new FormUrlEncodedContent(loginParams);

                try
                {
                    var loginResponse = await httpClient.SendAsync(loginRequest);

                    if (loginResponse.StatusCode == HttpStatusCode.Found)
                    {
                        updateSessionCookie(loginResponse.Headers);
                        // logged in
                    }
                    else
                    {
                        throw new InvalidOperationException("Login failed");
                    }
                }
                finally
                {
                    getRootRequest = null;
                    htmlDoc = null;
                    loginFormElement = null;
                    csrfHidden = null;
                    csrfToken = null;
                    loginParams = null;
                    loginRequest = null;
                    GC.Collect();
                }
            }
        }

        /// <summary>
        /// Gets system gateways root configuration
        /// </summary>
        /// <returns></returns>
        public async Task<SystemGateways> GetSystemGateways()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{rootUrl}system_gateways.php");
            setSessionHeaders(request.Headers);

            var response = await httpClient.SendAsync(request);
            var result = response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            updateSessionCookie(response.Headers);

            return new SystemGateways(this, body);
        }

        public async Task<StatusInterfaces> GetStatusInterfacesAsync()
        {
            return new StatusInterfaces(this, await StatusInterfaces.GetStatusInterfacesContent(this));
        }
        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    httpClient?.Dispose();
                    sessionId?.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

        private static string secureStringToString(SecureString s)
        {
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(s);
                return Marshal.PtrToStringUni(valuePtr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }

    }
}
