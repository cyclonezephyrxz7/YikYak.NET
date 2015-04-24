using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace YikYak
{
    internal class Parse
    {
        #region Configuration Variables

        private static bool _initted = false;

        private static Guid _iid;
        private static String _objectID;
        private static bool _registered;

        #endregion

        #region Parse API Variables

        private static HttpClient HTTP_CLIENT;

        #endregion

        #region Helper Functions

        /// <summary>
        /// Generates a Nonce for Cryptographic Signing
        /// </summary>
        /// <returns></returns>
        private static long GenerateNonce()
        {
            Random r = new Random();
            byte[] buf = new byte[8];
            r.NextBytes(buf);
            return BitConverter.ToInt64(buf, 0);
        }

        /// <summary>
        /// Generates the OAuth Header required for all transactions with Parse
        /// for the YikYak client.
        /// </summary>
        /// <param name="api">The API destination (either 'create' or 'update')</param>
        /// <returns></returns>
        private static String GenerateOAuthHeader(String api)
        {
            long nonce = GenerateNonce(); // oauth_nonce
            int timestamp = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; // oauth_timestamp

            // Prepare the CryptographicKey
            IBuffer secret = CryptographicBuffer.ConvertStringToBinary(Config.PARSE_ENCRYPTION_KEY, BinaryStringEncoding.Utf8);
            MacAlgorithmProvider encrypter = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha1);
            CryptographicKey key = encrypter.CreateKey(secret);

            // Prepare the message (Add OAuth parameters lexographically)
            String method = "POST";
            String url = "https://api.parse.com/2/" + api;

            StringBuilder oauthParams = new StringBuilder();
            oauthParams.Append("oauth_consumer_key=").Append(Config.PARSE_CONSUMER_ID).Append("&");
            oauthParams.Append("oauth_nonce=").Append(nonce).Append("&");
            oauthParams.Append("oauth_signature_method=HMAC-SHA1&");
            oauthParams.Append("oauth_timestamp=").Append(timestamp).Append("&");
            oauthParams.Append("oauth_version=1.0");

            String strMessage = WebUtility.UrlEncode(method) + "&" + WebUtility.UrlEncode(url) + "&" + WebUtility.UrlEncode(oauthParams.ToString());

            IBuffer message = CryptographicBuffer.ConvertStringToBinary(strMessage, BinaryStringEncoding.Utf8);

            // Compute the hash
            IBuffer hash = CryptographicEngine.Sign(key, message);
            String strHash = WebUtility.UrlEncode(CryptographicBuffer.EncodeToBase64String(hash));

            // Create the POST request
            StringBuilder authHeader = new StringBuilder("OAuth ");
            authHeader.Append("oauth_consumer_key=\"").Append(Config.PARSE_CONSUMER_ID).Append("\", ");
            authHeader.Append("oauth_version=\"1.0\", ");
            authHeader.Append("oauth_signature_method=\"HMAC-SHA1\", ");
            authHeader.Append("oauth_timestamp=\"").Append(timestamp).Append("\", ");
            authHeader.Append("oauth_nonce=\"").Append(nonce).Append("\", ");
            authHeader.Append("oauth_signature=\"").Append(strHash).Append("\"");

            return authHeader.ToString();
        }

        /// <summary>
        /// Clears and Sets the Authorization Header with the provided value.
        /// </summary>
        /// <param name="value">The content of the Authorization Header</param>
        /// <returns>True on success, False otherwise</returns>
        private static bool SetOAuthHeader(String value)
        {
            if (!_initted || HTTP_CLIENT == null) return false;

            try
            {
                if (HTTP_CLIENT.DefaultRequestHeaders.ContainsKey("Authorization"))
                    HTTP_CLIENT.DefaultRequestHeaders.Remove("Authorization");

                HTTP_CLIENT.DefaultRequestHeaders.TryAppendWithoutValidation("Authorization", value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the generic JSON Object which is the content of the POST request.
        /// Takes the non-generic JSON Object which represents the 'data' field to be
        /// used in creation.
        /// </summary>
        /// <param name="dataObject">The call-specific JSON Object</param>
        /// <returns></returns>
        private static JsonObject BuildContent(JsonObject dataObject)
        {
            JsonObject jsonContent = new JsonObject();
            jsonContent.Add("v", JsonValue.CreateStringValue("a1.7.1"));
            jsonContent.Add("appBuildVersion", JsonValue.CreateStringValue("57"));
            jsonContent.Add("osVersion", JsonValue.CreateStringValue("4.4.4"));
            jsonContent.Add("iid", JsonValue.CreateStringValue(_iid.ToString()));
            jsonContent.Add("classname", JsonValue.CreateStringValue("_Installation"));

            jsonContent.Add("data", dataObject);
            jsonContent.Add("appDisplayVersion", JsonValue.CreateStringValue(Config.API_VERSION));
            jsonContent.Add("uuid", JsonValue.CreateStringValue(Guid.NewGuid().ToString()));

            return jsonContent;
        }

        #endregion

        public enum Result
        {
            FAILURE,
            SUCCESS,
            ALREADY_REGISTERED,
            ALREADY_INITTED,
            NOT_INITTED
        }

        /// <summary>
        /// Initializes the Parse singleton.
        /// </summary>
        /// <returns>True on success, False otherwise</returns>
        public static Result Init()
        {
            if (_initted) return Result.ALREADY_INITTED;

            Result res = Result.SUCCESS;

            // First check SettingsManager to see if we have already installed
            _iid = Settings.Parse_IID;
            _objectID = Settings.Parse_OID;
            _registered = Settings.Parse_Success;

            if (_iid.Equals(Guid.Empty) || String.IsNullOrWhiteSpace(_objectID) || !_registered)
            {
                _iid = Guid.NewGuid();
                _objectID = null;
                _registered = false;

                // Create & Configure the HTTP Client
                try
                {
                    HttpBaseProtocolFilter bpf = new HttpBaseProtocolFilter();
#if DEBUG
                    bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
                    bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
                    bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
#endif
                    bpf.CacheControl.ReadBehavior = Windows.Web.Http.Filters.HttpCacheReadBehavior.MostRecent;
                    bpf.CacheControl.WriteBehavior = Windows.Web.Http.Filters.HttpCacheWriteBehavior.NoCache;

                    HTTP_CLIENT = new HttpClient(bpf);

                    HTTP_CLIENT.DefaultRequestHeaders.CacheControl.Clear();
                    HTTP_CLIENT.DefaultRequestHeaders.UserAgent.Clear();
                    HTTP_CLIENT.DefaultRequestHeaders.UserAgent.ParseAdd("Parse Android SDK 1.7.1 (com.yik.yak/57) API Level 19");
                    HTTP_CLIENT.DefaultRequestHeaders.AcceptEncoding.Clear();
                    HTTP_CLIENT.DefaultRequestHeaders.AcceptEncoding.Add(new HttpContentCodingWithQualityHeaderValue("gzip"));
                }
                catch (Exception)
                {
                    return Result.FAILURE;
                }

            }
            else
            {
                res = Result.ALREADY_REGISTERED;
            }

            _initted = true;
            return res;
        }

        /// <summary>
        /// Performs an 'Installation'/Create request to the Parse API.
        /// Prerequisite: Init() has already been called.
        /// </summary>
        /// <returns>Result.SUCCESS on success, Result.ALREADY_REGISTERED if it would appear you are already registered, or some failure Result otherwise.</returns>
        async public static Task<Result> Create()
        {
            if (!_initted) return Result.NOT_INITTED;
            if (_registered) return Result.ALREADY_REGISTERED;

            String authHeader = GenerateOAuthHeader("create");
            SetOAuthHeader(authHeader);

            // Prepare the JSON Object content
            JsonObject dataObject = new JsonObject();
            dataObject.Add("appIdentifier", JsonValue.CreateStringValue("com.yik.yak"));
            dataObject.Add("appName", JsonValue.CreateStringValue("Yik Yak"));
            dataObject.Add("installationId", JsonValue.CreateStringValue(_iid.ToString()));

            String tzID = "America/New_York";
            try
            {
                tzID = DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;
            }
            catch
            {
                // Some error, I guess (Shouldn't be much of a problem, actually)
            }

            dataObject.Add("timeZone", JsonValue.CreateStringValue(tzID));

            dataObject.Add("parseVersion", JsonValue.CreateStringValue("1.7.1"));
            dataObject.Add("appVersion", JsonValue.CreateStringValue(Config.API_VERSION));
            dataObject.Add("deviceType", JsonValue.CreateStringValue("android"));

            JsonObject jsonContent = BuildContent(dataObject);

            HttpStringContent requestContent = new HttpStringContent(jsonContent.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

            Uri dest = new Uri(Config.PARSE_URI + "create");

            try
            {
                HttpResponseMessage resp = await HTTP_CLIENT.PostAsync(dest, requestContent);

                String strResponse = await resp.Content.ReadAsStringAsync();
                JsonValue jsonResponse = JsonValue.Parse(strResponse);
                JsonObject objResponse = jsonResponse.GetObject();

                if (objResponse.ContainsKey("result"))
                {
                    JsonObject objResult = objResponse.GetNamedObject("result");
                    if (objResult.ContainsKey("data"))
                    {
                        JsonObject objData = objResult.GetNamedObject("data");
                        if (objData.ContainsKey("objectId"))
                        {
                            _objectID = objData.GetNamedString("objectId");

                            // Set SettingsManager values
                            Settings.Parse_IID = _iid;
                            Settings.Parse_OID = _objectID;
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
                else
                {
                    throw new Exception();
                }

                return Result.SUCCESS;
            }
            catch (Exception)
            {
                return Result.FAILURE;
            }
        }

        /// <summary>
        /// Performs an 'Update'/Registration request to the Parse API to grant POST access to a given userID.
        /// Prerequisite: Create() has already been called.
        /// </summary>
        /// <param name="userID">The YikYak UserID to register for POST access</param>
        /// <returns>Result.SUCCESS on success, Result.ALREADY_REGISTERED if it would appear you are already registered, or some failure Result otherwise.</returns>
        async public static Task<Result> Register(String userID)
        {
            if (!_initted || String.IsNullOrWhiteSpace(_objectID) || _iid.Equals(Guid.Empty)) return Result.NOT_INITTED;
            if (_registered) return Result.ALREADY_REGISTERED;

            // ONLY PERFORMING THE SECOND UPDATE CALL (Because GCM is useless on Windows Phone)
            String authHeader = GenerateOAuthHeader("update");
            SetOAuthHeader(authHeader);

            // Prepare the JSON Object
            JsonObject dataObject = new JsonObject();

            JsonObject channelsObject = new JsonObject();
            JsonArray objectsArray = new JsonArray();
            objectsArray.Add(JsonValue.CreateStringValue("c" + userID + "c"));
            channelsObject.Add("objects", objectsArray);
            channelsObject.Add("__op", JsonValue.CreateStringValue("AddUnique"));

            dataObject.Add("channels", channelsObject);
            dataObject.Add("objectId", JsonValue.CreateStringValue(_objectID));

            JsonObject jsonContent = BuildContent(dataObject);

            HttpStringContent requestContent = new HttpStringContent(jsonContent.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

            Uri dest = new Uri(Config.PARSE_URI + "update");

            HttpResponseMessage resp = await HTTP_CLIENT.PostAsync(dest, requestContent);

            try
            {
                String strResponse = await resp.Content.ReadAsStringAsync();
                JsonValue jsonResponse = JsonValue.Parse(strResponse);
                JsonObject objResponse = jsonResponse.GetObject();

                if (objResponse.ContainsKey("result"))
                {
                    JsonObject objResult = objResponse.GetNamedObject("result");
                    if (objResult.ContainsKey("data"))
                    {
                        JsonObject objData = objResult.GetNamedObject("data");
                        if (objData.ContainsKey("channels"))
                        {
                            JsonArray channelsArray = objData.GetNamedArray("channels");
                            if (channelsArray.Count != 1 || !channelsArray.GetStringAt(0).Equals("c" + userID + "c"))
                            {
                                throw new Exception();
                            }
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
                else
                {
                    throw new Exception();
                }

                return Result.SUCCESS;
            }
            catch (Exception)
            {
                return Result.FAILURE;
            }
        }
    }
}
