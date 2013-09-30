using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net.Http;

namespace rsPlurkLib
{
    /// <summary>
    /// Provides OAuth authentication service for rsPlurk. This class cannot be inherited.
    /// </summary>
    internal sealed class OAuthInstance : IOAuthClient
    {
        #region "Fields"
        private OAuthToken token = new OAuthToken();
        private string lastNonce = null;
        private string lastTimestamp = null;
        #endregion

        #region "Constant Fields"
        private static string cAppKey = "";         // Fill the application key you acquired
        private static string cAppSecret = "";      // Fill the application secret also
        private static string cReqTokenUrl = "http://www.plurk.com/OAuth/request_token";
        private static string cGrantUrl = "http://www.plurk.com/OAuth/authorize";
        private static string cExchangeUrl = "http://www.plurk.com/OAuth/access_token";
        private static string cApiBaseUrl = "http://www.plurk.com/APP/";
        private static string cUnreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.~";
        private static DateTime cTimestampBase = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        #endregion

        #region "Constructor"
        public OAuthInstance()
        {
        }
        #endregion

        #region "Methods"
        /// <summary>
        /// Retrieves a request token from Plurk and stores it in the current OAuthInstance.
        /// </summary>
        /// <exception cref="WebException">Connection problems occured.</exception>
        public async void GetRequestToken()
        {
            Dictionary<string, string> param = new Dictionary<string, string>()
                { { "oauth_callback", "oob"} }; // Plurk seems to omit this parameter
            
            try
            {
                var responseText = await CreateRequest(cReqTokenUrl, param);
                Dictionary<string, string> data = HttpUtility.ParseQueryString<Dictionary<string, string>>(responseText);
                token.Content = data["oauth_token"];
                token.Secret = data["oauth_token_secret"];
                token.State = OAuthTokenType.Temporary;
                
            }
            catch (WebException ex)
            {
                WebExceptionHelper(ex);
                throw new OAuthException("HTTP connection error during OAuth request", ex);
            }

        }

        /// <summary>
        /// After GetRequestToken() succeed, returns the URL to the authorization page.
        /// </summary>
        /// <exception cref="InvalidOperationException">Occurs when a valid request token 
        /// hasn't been obtained.</exception>
        public string GetAuthorizationUrl()
        {
            if (String.IsNullOrEmpty(token.Content)) throw new InvalidOperationException();
            return String.Format("{0}?oauth_token={1}", cGrantUrl, token.Content);
        }

        /// <summary>
        /// After user grants access to rsPlurk, retrieves the permanent access token.
        /// </summary>
        /// <param name="verifier">Verifier assigned by Plurk.</param>
        /// <exception cref="InvalidOperationException">Occurs when a valid request token 
        /// hasn't been obtained.</exception>
        /// <exception cref="UnauthorizedAccessException">Occurs when the token has expired 
        /// or the verifier specified is not valid.</exception>
        /// <exception cref="WebException">Connection problems occured.</exception>
        public async void GetAccessToken(string verifier)
        {
            EnsureTokenState();
            Dictionary<string, string> param = new Dictionary<string, string>()
                { { "oauth_token", token.Content}, {"oauth_verifier", verifier} };
            
            try
            {
                var responseText = await CreateRequest(cExchangeUrl, param);
                Dictionary<string, string> data = HttpUtility.ParseQueryString<Dictionary<string, string>>(responseText);
                token.Content = data["oauth_token"];
                token.Secret = data["oauth_token_secret"];
                token.State = OAuthTokenType.Permanent;
            }
            catch (WebException ex)
            {
                WebExceptionHelper(ex);
                throw new OAuthException("HTTP connection error during OAuth request", ex);
            }

        }

        /// <summary>
        /// Sends a request to specified URL, using specified token and arguments.
        /// </summary>
        /// <param name="apiPath">Relative path of target API.</param>
        /// <param name="args">Additional arguments. Must not start with 'oauth'.</param>
        /// <exception cref="InvalidOperationException">Occurs when a valid request token 
        /// hasn't been obtained.</exception>
        /// <exception cref="UnauthorizedAccessException">Occurs when the token has expired 
        /// or the verifier specified is not valid.</exception>
        /// <exception cref="WebException">Connection problems occured.</exception>
        public async Task<string> SendRequest(string apiPath, Dictionary<string, string> args)
        {
            EnsureTokenState();
            Dictionary<string, string> param = new Dictionary<string, string>() { { "oauth_token", token.Content } };

            if (args != null)
                foreach (string key in args.Keys)
                    if (!key.StartsWith("oauth_")) param.Add(key, args[key]);

            try
            {
                return await CreateRequest(cApiBaseUrl + apiPath, param);
            }
            catch (WebException ex)
            {
                WebExceptionHelper(ex);
                throw new OAuthException("HTTP connection error during OAuth request", ex);
            }
            catch (IOException ex)
            {
                throw new OAuthException("Network I/O error during OAuth request", ex);
            }
        }
        #endregion

        #region "Private Worker Functions"
        /// <summary>
        /// Gets the OAuth standard encoding of a string.
        /// </summary>
        /// <param name="source">String to be encoded.</param>
        /// <returns></returns>
        private static string UrlEncode(string source)
        {
            if (String.IsNullOrEmpty(source)) return null;
            byte[] chars = Encoding.UTF8.GetBytes(source);
            StringBuilder sb = new StringBuilder();
            foreach (byte c in chars)
                if (cUnreservedChars.IndexOf((char)c) >= 0)
                    sb.Append((char)c);
                else
                    sb.Append(String.Format("%{0:X2}", (int)c));
            return sb.ToString();
        }

        /// <summary>
        /// Creates a HttpWebRequest with specified OAuth parameters and URL.
        /// </summary>
        /// <param name="uri">The request target URI.</param>
        /// <param name="param">The OAuth parameters to use.</param>
        /// <returns>The created HttpWebRequest.</returns>
        private async Task<string> CreateRequest(string uri, Dictionary<string, string> param)
        {
            var webClient = new HttpClient();

            var form = GetMultipartFormDataContent(param);
            var response = await webClient.PostAsync(uri, form);

            response.EnsureSuccessStatusCode();
 
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Generates a HMAC-SHA1 signature from the specified data.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="uri">Normalized service URI.</param>
        /// <param name="param">Parameters used in the request.</param>
        /// <returns></returns>
        private string GetSignature(string method, string uri, Dictionary<string, string> param)
        {
            // Build up base string
            StringBuilder sb = new StringBuilder();
            sb.Append(method).Append('&').Append(UrlEncode(uri)).Append('&');

            // Sort the params
            List<string> keys = new List<string>(param.Keys);
            keys.Sort(StringComparer.InvariantCulture);

            string prefix = "";
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(prefix).Append(keys[i]).Append("%3D").Append(UrlEncode(UrlEncode(param[keys[i]])));
                prefix = "%26";
            }

            string source = sb.ToString();
            string key = UrlEncode(cAppSecret) + "&" + UrlEncode(token.Secret);

            HMACSHA1 hashProvider = new HMACSHA1(Encoding.UTF8.GetBytes(key));
            byte[] result = hashProvider.ComputeHash(Encoding.UTF8.GetBytes(source));
            
            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// Returns the number of seconds since January 1, 1970 00:00:00 GMT (a.k.a. Unix Time).
        /// </summary>
        private static string GetTimestamp()
        {
            return Math.Ceiling((DateTime.UtcNow - cTimestampBase).TotalSeconds).ToString();
        }

        /// <summary>
        /// Generates an unique string.
        /// </summary>
        private static string GetNonce()
        {
            return new Random().Next(1, 99999999).ToString("d8"); // Must be 64bit, but Plurk didn't mention
        }

        /// <summary>
        /// Makes sure the token is state-correct.
        /// </summary>
        private void EnsureTokenState()
        {
            if (String.IsNullOrEmpty(token.Content))
                throw new InvalidOperationException("A valid token is required");
        }

        /// <summary>
        /// Processes exception occured during authorization.
        /// </summary>
        /// <param name="ex">Thrown exception.</param>
        private void WebExceptionHelper(WebException ex)
        {
            HttpWebResponse resp = ex.Response as HttpWebResponse;
            if (resp != null)

                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new OAuthAuthorizationException(resp.StatusDescription, ex);

                else if (resp.StatusCode == HttpStatusCode.BadRequest)
                    try
                    {
                        string response;
                        using (var reader = new StreamReader(resp.GetResponseStream()))
                            response = reader.ReadToEnd();

                        if (response == "40004:invalid timestamp or nonce")
                            throw new OAuthNonceException(lastNonce, lastTimestamp);
                        else
                            throw new OAuthRequestException(response, ex);
                    }
                    finally { resp.Close(); }
        }

        private MultipartFormDataContent GetMultipartFormDataContent(Dictionary<string, string> param)
        {
            MultipartFormDataContent result = new MultipartFormDataContent();
            
            // Build up header
            StringBuilder sb = new StringBuilder();
            sb.Append("OAuth realm=\"\"");
            foreach (string key in param.Keys)
            {
                result.Add(new StringContent(param[key]), key);
                if (key.StartsWith("oauth_")) // Possible additional parameters
                    sb.Append(", ").Append(key).Append("=\"").Append(UrlEncode(param[key])).Append("\"");
            }
            result.Headers.Add("Authorization", sb.ToString());
            result.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            return result;
        }

        #endregion

        #region "Properties"
        /// <summary>
        /// Gets or sets the current token container.
        /// </summary>
        /// <remarks>This implementation currently only accepts instances of OAuthToken.</remarks>
        public IOAuthToken Token
        {
            get { return token; }
            set {
                OAuthToken toSet = value as OAuthToken;
                if (value == null) throw new ArgumentException();
                token = toSet;
            }
        }
        #endregion
    }

    /// <summary>
    /// Stores a OAuth token. This class cannot be inherited.
    /// </summary>
    public sealed class OAuthToken : IOAuthToken
    {
        #region "Constructor"
        /// <summary>
        /// Creates an empty token container.
        /// </summary>
        public OAuthToken()
        {
            Content = null;
            Secret = null;
            State = OAuthTokenType.Empty;
        }

        /// <summary>
        /// Creates a token container using specified data.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <param name="secret">Token secret.</param>
        /// <param name="state">Token state.</param>
        public OAuthToken(string token, string secret, OAuthTokenType state)
        {
            Content = token;
            Secret = secret;
            State = state;
        }
        #endregion

        #region "Properties"
        /// <summary>
        /// Gets or sets the token string of this token.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets the secret of this token.
        /// </summary>
        public string Secret { get; set; }

        /// <summary>
        /// Gets or sets the current token state.
        /// </summary>
        public OAuthTokenType State { get; set; }
        #endregion

    }

    /// <summary>
    /// Represents the state of an OAuth token.
    /// </summary>
    public enum OAuthTokenType
    {
        Empty = 0,
        Temporary,
        Permanent
    }
}
