using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Geolocation;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace YikYak
{
    /// <summary>
    /// Internal Helper Functions
    /// </summary>
    public static class Helper
    {
        private static Geolocator GEOLOCATOR;

        /// <summary>
        /// Safely parses out a desired JSON Value as the provided type, if it is possible.
        /// On failure, it will return a default value (and will NOT raise an exception).
        /// </summary>
        /// <param name="obj">The JsonObject containing the information to retrieve</param>
        /// <param name="field">The named field within the JsonObject which we want the value of</param>
        /// <param name="rType">The type which we want to convert the value to</param>
        /// <param name="defValue">A default value to return in case of failure</param>
        /// <returns>The value contained within the JsonObject for a given field, in a given type if it is possible to parse it that way</returns>
        internal static Object GetJSONValue(JsonObject obj, String field, ExpectedReturnType rType, Object defaultReturn)
        {
            if (!obj.ContainsKey(field)) return defaultReturn;

            JsonValue val = obj.GetNamedValue(field);
            switch (rType)
            {
                case ExpectedReturnType.STRING:
                    if (val.ValueType == JsonValueType.String) return val.GetString();
                    else if (val.ValueType == JsonValueType.Null) return "";
                    else if (val.ValueType == JsonValueType.Number) return val.GetNumber().ToString();
                    else if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean().ToString();
                    break;

                case ExpectedReturnType.INT:
                    if (val.ValueType == JsonValueType.Number) return (int)val.GetNumber();
                    else if (val.ValueType == JsonValueType.String)
                    {
                        int tmp = 0;
                        if (int.TryParse(val.GetString(), out tmp))
                        {
                            return tmp;
                        }
                    }
                    break;

                case ExpectedReturnType.DOUBLE:
                    if (val.ValueType == JsonValueType.Number) return val.GetNumber();
                    else if (val.ValueType == JsonValueType.String)
                    {
                        double tmp = 0.0;
                        if (double.TryParse(val.GetString(), out tmp))
                        {
                            return tmp;
                        }
                    }
                    break;

                case ExpectedReturnType.DATETIME:
                    if (val.ValueType == JsonValueType.String)
                    {
                        DateTime tmp = new DateTime();
                        if (DateTime.TryParseExact(val.GetString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out tmp) || // Yaks/Comments
                            DateTime.TryParse(val.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out tmp)) // Notifications
                        {
                            // This MAY just be a weird patch, but I think the YikYak API sends times formatted for DST ... so if it isn't DST, subtract an hour
                            if (!TimeZoneInfo.Local.IsDaylightSavingTime(tmp))
                                tmp = tmp.Subtract(new TimeSpan(1, 0, 0));
                            return tmp;
                        }
                    }
                    break;

                case ExpectedReturnType.BOOLEAN:
                    if (val.ValueType == JsonValueType.Boolean) return val.GetBoolean();
                    else if (val.ValueType == JsonValueType.Number) return val.GetNumber() != 0;
                    else if (val.ValueType == JsonValueType.String)
                    {
                        bool tmp = false;
                        if (bool.TryParse(val.GetString(), out tmp))
                        {
                            return tmp;
                        }
                        else
                        {
                            String str = val.GetString();
                            if (str.Equals("true", StringComparison.CurrentCultureIgnoreCase)) return true;
                            else if (str.Equals("false", StringComparison.CurrentCultureIgnoreCase)) return false;
                            else
                            {
                                // Lastly try to parse it as a number
                                int tmpI = 0;
                                if (int.TryParse(str, out tmpI)) return tmpI != 0;

                                double tmpD = 0.0;
                                if (double.TryParse(str, out tmpD)) return tmpD != 0.0;
                            }
                        }
                    }
                    break;

                case ExpectedReturnType.VOTESTATUS:
                    if (val.ValueType == JsonValueType.Number)
                    {
                        if (val.GetNumber() < 0) return VoteStatus.DISLIKED;
                        else if (val.GetNumber() > 0) return VoteStatus.LIKED;
                        else return VoteStatus.NO_VOTE;
                    }
                    else if (val.ValueType == JsonValueType.String)
                    {
                        int parsedVal = 0;
                        if (int.TryParse(val.GetString(), out parsedVal))
                        {
                            if (parsedVal < 0) return VoteStatus.DISLIKED;
                            else if (parsedVal > 0) return VoteStatus.LIKED;
                            else return VoteStatus.NO_VOTE;
                        }
                    }
                    break;
            }

            return defaultReturn;
        }

        /// <summary>
        /// Builds a URI containing all necessary QUERY values (in-order) based on the API.
        /// Null values will be excluded.
        /// </summary>
        /// <returns></returns>
        internal static Uri BuildURI(Uri baseURI, String dest, double? accuracy, String deviceID, String token, 
                                    double? latitude, double? longitude, String userID, double? userLatitude,
                                    double? userLongitude, String messageID, String commentID, int? peekID, String reason)
        {
            UriBuilder builder = new UriBuilder(baseURI);

            if (!builder.Path.EndsWith("/")) builder.Path += "/";

            builder.Path += dest;

            StringBuilder query = new StringBuilder("?");

            if (accuracy != null) query.Append("accuracy=").Append(accuracy.Value.ToString("F1")).Append("&");
            if (deviceID != null) query.Append("deviceID=").Append(deviceID).Append("&");
            if (latitude != null) query.Append("lat=").Append(latitude.Value.ToString("F7")).Append("&");
            if (longitude != null) query.Append("long=").Append(longitude.Value.ToString("F7")).Append("&");
            if (token != null) query.Append("token=").Append(token).Append("&");
            if (commentID != null) query.Append("commentID=").Append(WebUtility.UrlEncode(commentID)).Append("&");
            if (messageID != null) query.Append("messageID=").Append(WebUtility.UrlEncode(messageID)).Append("&");
            if (reason != null) query.Append("reason=").Append(WebUtility.UrlEncode(reason)).Append("&");
            if (peekID != null) query.Append("peekID=").Append(peekID.Value).Append("&");
            if (userID != null) query.Append("userID=").Append(WebUtility.UrlEncode(userID)).Append("&");
            if (userLatitude != null) query.Append("userLat=").Append(userLatitude.Value.ToString("F7")).Append("&");
            if (userLongitude != null) query.Append("userLong=").Append(userLongitude.Value.ToString("F7")).Append("&");

            query.Append("version=" + Config.API_VERSION);

            // Now we must put on the "salt" and "hash" values [as per the YikYak app]
            // Salt is simply the Time since epoch (1/1/1970 00:00:00) in SECONDS
            // Hash is a bit more complicated (wasn't successfully decompiled)
            //  It is a hash of the string "/api/<API_ENDPOINT><PARAMS><SALT>"
            //  I got the private key & algorithm (SHA1) from YikYakTerminal project on GitHub

            int salt = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

            IBuffer secret = CryptographicBuffer.ConvertStringToBinary(Config.URI_ENCRYPTION_KEY, BinaryStringEncoding.Utf8);

            String rawMessage = builder.Uri.ToString();
            rawMessage = rawMessage.Substring(rawMessage.IndexOf("/api"));
            rawMessage += query.ToString() + salt.ToString();

            IBuffer message = CryptographicBuffer.ConvertStringToBinary(rawMessage, BinaryStringEncoding.Utf8);
            MacAlgorithmProvider encrypter = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha1);
            CryptographicKey key = encrypter.CreateKey(secret);
            IBuffer hash = CryptographicEngine.Sign(key, message);


            query.Append("&salt=").Append(salt.ToString());
            query.Append("&hash=").Append(WebUtility.UrlEncode(CryptographicBuffer.EncodeToBase64String(hash)));


            // Remove leading '?' before setting as Query
            query.Remove(0, 1);
            builder.Query = query.ToString();

            return builder.Uri;
        }

        /// <summary>
        /// Builds the query-string like content for a POST Message.
        /// </summary>
        /// <param name="fields">An array containing the fields of the content information</param>
        /// <param name="values">An array containing the values of the content information</param>
        /// <returns></returns>
        internal static HttpStringContent BuildPOSTContent(String[] fields, String[] values)
        {
            if (fields.Length != values.Length) return null;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i] != null && values[i] != null)
                {
                    builder.Append(fields[i]);
                    builder.Append("=");
                    builder.Append(WebUtility.UrlEncode(values[i]));
                    builder.Append("&");
                }
            }

            return new HttpStringContent(builder.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/x-www-form-urlencoded; charset=UTF-8");
        }

        /// <summary>
        /// Performs a GET Request to the desired Uri with a given timeout
        /// </summary>
        /// <param name="client">The Windows.Web.Http.HttpClient to use to make the request</param>
        /// <param name="destination">The Uri desination for the request</param>
        /// <param name="ct">A cancellation token, if the request should time-out (otherwise CancellationToken.None)</param>
        async internal static Task<Response<String>> Get(HttpClient client, Uri destination, CancellationToken ct)
        {
            try
            {
                HttpResponseMessage resp;

                if (ct.Equals(CancellationToken.None))
                    resp = await client.GetAsync(destination);
                else
                    resp = await client.GetAsync(destination).AsTask(ct);

                if (resp.IsSuccessStatusCode)
                {
                    String content = await resp.Content.ReadAsStringAsync();
                    return new Response<String>() { Result = Result.SUCCESS, Return = content };
                }
                else
                {
                    Debug.WriteLine("YikYak.Helper: GET [{0}] Failed [Status={1}]", destination.AbsolutePath, resp.StatusCode);
                    return new Response<String>() { Result = Result.WEB_REQUEST_FAILED, Return = null };
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("YikYak.Helper: GET [{0}] Timed Out", destination.AbsolutePath);
                return new Response<String>() { Result = Result.WEB_REQUEST_TIMED_OUT, Return = null };
            }
            catch
            {
                Debug.WriteLine("YikYak.Helper: GET [{0}] Failed", destination.AbsolutePath);
                return new Response<String>() { Result = Result.WEB_REQUEST_FAILED, Return = null };
            }
        }

        /// <summary>
        /// Performs a POST Request to the desired Uri with a given timeout
        /// </summary>
        /// <param name="client">The Windows.Web.Http.HttpClient to use to make the request</param>
        /// <param name="destination">The Uri destination for the request</param>
        /// <param name="content">The content to provide with the POST request</param>
        /// <param name="ct">A cancellation token, if the request should time-out (otherwise CancellationToken.None)</param>
        /// <returns></returns>
        async internal static Task<Response<String>> Post(HttpClient client, Uri destination, IHttpContent body, CancellationToken ct)
        {
            try
            {
                HttpResponseMessage resp;

                if (ct.Equals(CancellationToken.None))
                    resp = await client.PostAsync(destination, body);
                else
                    resp = await client.PostAsync(destination, body).AsTask(ct);

                if (resp.IsSuccessStatusCode)
                {
                    String content = await resp.Content.ReadAsStringAsync();
                    return new Response<String>() { Result = Result.SUCCESS, Return = content };
                }
                else
                {
                    Debug.WriteLine("YikYak.Helper: POST [{0}] Failed [Status={1}]", destination.AbsolutePath, resp.StatusCode);
                    return new Response<String>() { Result = Result.WEB_REQUEST_FAILED, Return = null };
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("YikYak.Helper: POST [{0}] Timed Out", destination.AbsolutePath);
                return new Response<String>() { Result = Result.WEB_REQUEST_TIMED_OUT, Return = null };
            }
            catch
            {
                Debug.WriteLine("YikYak.Helper: POST [{0{] Failed", destination.AbsolutePath);
                return new Response<String>() { Result = Result.WEB_REQUEST_FAILED, Return = null };
            }
        }

        /// <summary>
        /// Requests the device's current location
        /// </summary>
        /// <param name="accuracyInMeters">The desired accuracy in meters for the current call</param>
        /// <param name="timeout">The desired timeout for the asynchronous operation</param>
        /// <returns>A Location on success, or null if it timed-out or failed</returns>
        async public static Task<Location> GetLocation(uint? accuracyInMeters, TimeSpan timeout)
        {
            if (GEOLOCATOR == null || GEOLOCATOR.DesiredAccuracyInMeters != accuracyInMeters)
            {
                GEOLOCATOR = new Geolocator();
                GEOLOCATOR.DesiredAccuracyInMeters = accuracyInMeters;
            }
            
            CancellationTokenSource cts = new CancellationTokenSource();
            
            try
            {
                if (timeout != TimeSpan.Zero)
                    cts.CancelAfter(timeout);

                Geoposition gp = await GEOLOCATOR.GetGeopositionAsync().AsTask(cts.Token);

                return new Location(gp);
            }
            catch
            {
                Debug.WriteLine("YikYak.Helper: GetLocation Timed Out.");
                return null;
            }
            finally
            {
                cts.Dispose();
            }
        }

        /// <summary>
        /// Parse out Yaks from a JSON Object that is supposedly containing them.
        /// </summary>
        /// <param name="root">The JSON Object in which the messages appear immediately.</param>
        /// <returns>An array of Yaks if there are any, null otherwise</returns>
        internal static Yak[] GetYaksFromJSON(JsonObject root)
        {
            if (root == null) return null;

            if (root.ContainsKey("messages"))
            {
                JsonArray messageArray = root.GetNamedArray("messages");
                List<Yak> ret = new List<Yak>();

                Yak prev = null; // For my hacky solution to my layout problem

                for (uint i = 0; i < messageArray.Count; i++)
                {
                    Yak tmp = Yak.Create(messageArray.GetObjectAt(i));
                    if (tmp != null) ret.Add(tmp);

                    if (prev != null) tmp._prev = prev;
                    prev = tmp;
                }

                return ret.ToArray();
            }

            return null;
        }
    }
}
