using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.System.Profile;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace YikYak
{
    /// <summary>
    /// Singleton object representing the publicly accessible YikYak API.
    /// </summary>
    public class API
    {
        #region API Configuration Variables

        public Uri API_URI;
        private DateTime DATETIME_API_STARTED;
        private HttpClient HTTP_CLIENT;

        private bool POST_ACCESS = false;
        private bool CAN_CHANGE_VOTE = true;
        private String[] DANGER_WORDS;
        private String DANGER_WORDS_MESSAGE;
        private TimeSpan THRESHOLD_LOCATION_TIMESPAN = new TimeSpan(0, 10, 0);

        #endregion

        #region Properties

        public String UserID { get; private set; }

        public UserIDGenerationMethod UserIDGenerationMethod { get; set; }

        public Location Location { get; private set; }

        public int UnreadNotifications { get; private set; }

        public PeekLocation[] FeaturedLocations { get; private set; }

        public PeekLocation[] PeekLocations { get; private set; }

        public BaseLocation[] CustomPeekLocations 
        {
            get
            {
                return Settings.CustomPeekLocations;
            }
            set
            {
                Settings.CustomPeekLocations = value;
            }
        }

        public int Yakarma { get; private set; }

        public String DangerWordsMessage { get { return DANGER_WORDS_MESSAGE; } }

        #endregion

        #region Singleton Constructor

        public static API Instance { get; private set; }

        async public static Task<API> GetInstance()
        {
            if (Instance == null)
            {
                Instance = new API();
                if (!await Instance.Initialize())
                    Instance = null; // Set to null on failure to initialize
            }

            return Instance;
        }

        /// <summary>
        /// A private parameterless constructor to prevent creation of this object
        /// by means other than calling the static GetInstance() method.
        /// </summary>
        private API() { }

        /// <summary>
        /// Private constructor for the API Instance.  Performs all necessary initialization
        /// of class variables and properties to ensure API calls can be made.
        /// </summary>
        async private Task<bool> Initialize()
        {
            #region Load Settings

            bool TravelMode = Settings.TravelMode_Enabled;

            double? savedLatitude  = Settings.SavedLocation_Latitude;
            double? savedLongitude = Settings.SavedLocation_Longitude;
            double? savedAccuracy  = Settings.SavedLocation_Accuracy;

            DateTime savedTimestamp = Settings.SavedLocation_Timestamp;

            API_URI = new Uri(Settings.API_BaseUrl);
            DANGER_WORDS_MESSAGE = Settings.DangerWords_Message;
            DANGER_WORDS = Settings.DangerWords_Array;
            UserID = Settings.UserID;

            bool ForceNewUserID = Settings.UserID_ForceNew;
            UserIDGenerationMethod = Settings.UserID_GenerationMethod;

            bool ParseRegistrationSuccess = Settings.Parse_Success;
            POST_ACCESS = ParseRegistrationSuccess;

            #endregion

            #region Set up HttpClient

            HttpBaseProtocolFilter bpf = new HttpBaseProtocolFilter();
#if DEBUG
            bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
            bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
            bpf.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
#endif
            bpf.CacheControl.ReadBehavior = Windows.Web.Http.Filters.HttpCacheReadBehavior.MostRecent;
            bpf.CacheControl.WriteBehavior = Windows.Web.Http.Filters.HttpCacheWriteBehavior.NoCache;

            HTTP_CLIENT = new HttpClient(bpf);

            HTTP_CLIENT.DefaultRequestHeaders.UserAgent.Clear();
            HTTP_CLIENT.DefaultRequestHeaders.TryAppendWithoutValidation("User-Agent", Config.USER_AGENT);
            HTTP_CLIENT.DefaultRequestHeaders.AcceptEncoding.Clear();
            HTTP_CLIENT.DefaultRequestHeaders.AcceptEncoding.Add(new HttpContentCodingWithQualityHeaderValue("gzip"));

            #endregion

            #region Initialize Remaining Variables

            DATETIME_API_STARTED = DateTime.Now;

            if (TravelMode && (!savedLatitude.HasValue || !savedLongitude.HasValue || !savedAccuracy.HasValue))
            {
                TravelMode = false;
                Settings.TravelMode_Enabled = false;
            }

            if (!TravelMode)
            {
                if (!savedLatitude.HasValue || !savedLongitude.HasValue || !savedAccuracy.HasValue)
                {
                    Debug.WriteLine("YikYak.API.Init: No Saved Location");

                    // Refresh Location
                    Location = await Helper.GetLocation(500, TimeSpan.Zero);

                    // Store it
                    Settings.SavedLocation_Latitude = Location.Latitude;
                    Settings.SavedLocation_Longitude = Location.Longitude;
                    Settings.SavedLocation_Accuracy = Location.Accuracy;
                    Settings.SavedLocation_Timestamp = Location.Timestamp;
                }
                else if ((DateTime.Now.ToUniversalTime() - savedTimestamp.ToUniversalTime()).Duration() > THRESHOLD_LOCATION_TIMESPAN)
                {
                    Debug.WriteLine("YikYak.API: Saved Location Aged");

                    // Refresh Location
                    Location = await Helper.GetLocation(500, new TimeSpan(0,0,5));

                    if (Location == null)
                        Location = new Location(savedLatitude.Value, savedLongitude.Value, savedAccuracy.Value, savedTimestamp);
                    else
                    {
                        // Store it
                        Settings.SavedLocation_Latitude = Location.Latitude;
                        Settings.SavedLocation_Longitude = Location.Longitude;
                        Settings.SavedLocation_Accuracy = Location.Accuracy;
                        Settings.SavedLocation_Timestamp = Location.Timestamp;
                    }
                }
                else
                {
                    Location = new Location(savedLatitude.Value, savedLongitude.Value, savedAccuracy.Value, savedTimestamp);
                }
            }

            Response<String> respAPIUrl = await GetAPIUrl();
            if (respAPIUrl.Result == Result.SUCCESS && !String.IsNullOrWhiteSpace(respAPIUrl.Return))
            {
                API_URI = new Uri(respAPIUrl.Return);
                Settings.API_BaseUrl = respAPIUrl.Return;
                Debug.WriteLine("YikYak.API.Init: API_BaseUrl is {0}", respAPIUrl.Return);
            }

            await UpdateConfig();

            if (String.IsNullOrWhiteSpace(UserID) || ForceNewUserID)
            {
                String _userID = GenerateUserID();
                if (!String.IsNullOrWhiteSpace(_userID))
                {
                    Response<bool> success = await RegisterUserID(_userID);
                    if (success.Result == Result.SUCCESS && success.Return)
                    {
                        UserID = _userID;
                        Settings.UserID = _userID;
                        Settings.UserID_ForceNew = false;
                    }
                    else
                    {
                        // Fatal Error
                        return false;
                    }
                }
                else
                {
                    // Fatal Error
                    return false;
                }
            }

            #endregion

            if (!ParseRegistrationSuccess)
            {
                // Register with Parse
                Parse.Result res = Parse.Init();
                if (res == Parse.Result.SUCCESS)
                {
                    res = await Parse.Create();
                    if (res == Parse.Result.SUCCESS)
                    {
                        res = await Parse.Register(UserID);
                        if (res == Parse.Result.SUCCESS || res == Parse.Result.ALREADY_REGISTERED)
                            POST_ACCESS = true;
                    }
                    else if (res == Parse.Result.ALREADY_REGISTERED)
                        POST_ACCESS = true;
                }
                else if (res == Parse.Result.ALREADY_REGISTERED)
                    POST_ACCESS = true;

                Settings.Parse_Success = POST_ACCESS;
            }


            await LogEvent(LogEventType.ApplicationDidBecomeActive);

            return true;
        }

        #endregion

        #region Private API Calls

        /// <summary>
        /// Generates a valid YikYak UserID based on this device's ASHWID
        /// </summary>
        /// <returns>A valid YikYak UserID given the class's UserIDGenerationMethod, or null on failure</returns>
        private String GenerateUserID()
        {
            String userID = null;

            try
            {
                HardwareToken packageSpecificToken = Windows.System.Profile.HardwareIdentification.GetPackageSpecificToken(null);
                IBuffer ASHWID = packageSpecificToken.Id;

                IBuffer generatedUniqueID = null;
                byte[] rawBytes;
                switch (UserIDGenerationMethod)
                {
                    case UserIDGenerationMethod.Standard:
                        generatedUniqueID = ASHWID;
                        break;

                    case UserIDGenerationMethod.Reversed:
                        rawBytes = new byte[ASHWID.Length];
                        CryptographicBuffer.CopyToByteArray(ASHWID, out rawBytes);
                        byte[] reversed = new byte[rawBytes.Length];

                        for (int i = 0; i < rawBytes.Length; i++)
                        {
                            reversed[i] = rawBytes[rawBytes.Length - i - 1];
                        }

                        generatedUniqueID = CryptographicBuffer.CreateFromByteArray(reversed);

                        break;

                    case UserIDGenerationMethod.Doubled:
                        rawBytes = new byte[ASHWID.Length];
                        CryptographicBuffer.CopyToByteArray(ASHWID, out rawBytes);
                        byte[] doubled = new byte[2 * rawBytes.Length];

                        for (int i = 0; i < 2 * rawBytes.Length; i++)
                        {
                            doubled[i] = rawBytes[i % rawBytes.Length];
                        }

                        generatedUniqueID = CryptographicBuffer.CreateFromByteArray(doubled);

                        break;

                    case UserIDGenerationMethod.Palindrome:
                        rawBytes = new byte[ASHWID.Length];
                        CryptographicBuffer.CopyToByteArray(ASHWID, out rawBytes);
                        byte[] palindrome = new byte[2 * rawBytes.Length];

                        for (int i = 0; i < rawBytes.Length; i++)
                        {
                            palindrome[i] = rawBytes[i];
                        }

                        for (int i = 0; i < rawBytes.Length; i++)
                        {
                            palindrome[i + rawBytes.Length] = rawBytes[rawBytes.Length - i - 1];
                        }

                        generatedUniqueID = CryptographicBuffer.CreateFromByteArray(palindrome);

                        break;
                }

                HashAlgorithmProvider hasher = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
                IBuffer hashed = hasher.HashData(generatedUniqueID);

                String strHashed = CryptographicBuffer.EncodeToHexString(hashed);

                userID = strHashed.Substring(0, 6) + strHashed.Substring(5, strHashed.Length - 6);

                return userID.ToUpper();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sends a request to YikYak to register the UserID
        /// </summary>
        /// <returns></returns>
        async private Task<Response<bool>> RegisterUserID(String ID)
        {
            // In 2.4.2, the Request now takes 3 UUID's:
            //  DeviceID - MD5 of Randomly Generated, x[5] == x[6], len == 32
            //  UserID - MD5 of Randomly Generated, x[5] == x[6], len == 32
            //  Token - MD5 of User Agent (without API_VERSION at the end)

            HashAlgorithmProvider hasher = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);

            // I'll generate DeviceID as an MD5 of [User]ID
            byte[] bytesDeviceID = new byte[ID.Length * sizeof(char)];
            System.Buffer.BlockCopy(ID.ToCharArray(), 0, bytesDeviceID, 0, bytesDeviceID.Length);
            IBuffer rawDeviceID = CryptographicBuffer.CreateFromByteArray(bytesDeviceID);

            IBuffer hashedDeviceID = hasher.HashData(rawDeviceID);

            String deviceID = CryptographicBuffer.EncodeToHexString(hashedDeviceID).ToUpper();
            deviceID = deviceID.Substring(0, 6) + deviceID.Substring(5, deviceID.Length - 6);

            String rawUserAgent = Config.USER_AGENT.Substring(0, Config.USER_AGENT.Length - Config.API_VERSION.Length - 1);
            byte[] bytesToken = new byte[rawUserAgent.Length * sizeof(char)];
            System.Buffer.BlockCopy(rawUserAgent.ToCharArray(), 0, bytesToken, 0, bytesToken.Length);
            IBuffer rawToken = CryptographicBuffer.CreateFromByteArray(bytesToken);

            IBuffer hashedToken = hasher.HashData(rawToken);
            String token = CryptographicBuffer.EncodeToHexString(hashedDeviceID).ToUpper();

            Uri req = Helper.BuildURI(API_URI, "registerUser", Location.Accuracy, deviceID, token, Location.Latitude,
                                      Location.Longitude, ID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);

            if (resp.Result == Result.SUCCESS && resp.Return.Equals("1"))
                return new Response<bool>() { Result = Result.SUCCESS, Return = true };
            else
                return new Response<bool>() { Result = resp.Result, Return = false };
        }

        /// <summary>
        /// Performs a GET request to determine which API Url we should be using.
        /// </summary>
        /// <returns></returns>
        async private Task<Response<String>> GetAPIUrl()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(1000);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, new Uri(Config.CLOUDFRONT_URI, "yikyakurl.json"), cts.Token);

            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    try
                    {
                        JsonObject config = respObj.GetNamedObject("configuration", null);
                        if (config != null)
                        {
                            String selectedEndpoint = (String)Helper.GetJSONValue(config, "default_endpoint", ExpectedReturnType.STRING, "");
                            JsonArray endpoints = config.GetNamedArray("endpoints", null);
                            if (endpoints != null)
                            {
                                for (uint i = 0; i < endpoints.Count; i++)
                                {
                                    JsonObject endpoint = endpoints.GetObjectAt(i);

                                    Debug.WriteLine("YikYak.API.GetAPIUrl: Endpoint({0})", endpoint.Stringify());

                                    double minLat = (double)Helper.GetJSONValue(endpoint, "min_latitude", ExpectedReturnType.DOUBLE, Double.MinValue);
                                    double maxLat = (double)Helper.GetJSONValue(endpoint, "max_latitude", ExpectedReturnType.DOUBLE, Double.MinValue);
                                    double minLong = (double)Helper.GetJSONValue(endpoint, "min_longitude", ExpectedReturnType.DOUBLE, Double.MinValue);
                                    double maxLong = (double)Helper.GetJSONValue(endpoint, "max_longitude", ExpectedReturnType.DOUBLE, Double.MinValue);

                                    if ((Location.Latitude >= minLat && Location.Latitude <= maxLat) && (Location.Longitude >= minLong && Location.Longitude <= maxLong))
                                    {
                                        selectedEndpoint = (String)Helper.GetJSONValue(endpoint, "url", ExpectedReturnType.STRING, "");
                                        Debug.WriteLine("YikYak.API.GetAPIUrl: URL({0})", selectedEndpoint);
                                        break;
                                    }
                                }
                            }

                            if (!String.IsNullOrWhiteSpace(selectedEndpoint))
                            {
                                if (!selectedEndpoint.EndsWith("/")) selectedEndpoint += "/";
                                return new Response<string>() { Result = Result.SUCCESS, Return = selectedEndpoint };
                            }
                        }
                    }
                    catch
                    {

                    }
                }
                
                return new Response<String>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
                return new Response<String>() { Result = resp.Result, Return = null };
        }
        
        /// <summary>
        /// Performs a GET request to update certain configuration variables (Danger Words [Message] and Vote Changing for now)
        /// </summary>
        /// <returns></returns>
        async private Task<Response<bool>> UpdateConfig()
        {
            CancellationTokenSource cts1 = new CancellationTokenSource();
            cts1.CancelAfter(1000);
            Response<String> resp1 = await Helper.Get(HTTP_CLIENT, new Uri(Config.CLOUDFRONT_URI, "yikyak-config.json"), cts1.Token);

            if (resp1.Result == Result.SUCCESS)
            {
                try
                {
                    JsonObject configRoot = JsonObject.Parse(resp1.Return);
                    JsonObject configConfig = configRoot.GetNamedObject("configuration");

                    JsonObject votingConfig = configConfig.GetNamedObject("voting");
                    JsonObject threatConfig = configConfig.GetNamedObject("threatWords");

                    // Now Get Threat Words
                    CancellationTokenSource cts2 = new CancellationTokenSource();
                    cts2.CancelAfter(1000);
                    Response<String> resp2 = await Helper.Get(HTTP_CLIENT, new Uri(threatConfig.GetNamedString("threatWordsUrl")), cts2.Token);
                    if (resp2.Result == Result.SUCCESS)
                    {
                        JsonObject wordsRoot = JsonObject.Parse(resp2.Return);
                        JsonObject wordsConfig = wordsRoot.GetNamedObject("configuration");

                        JsonArray expressions = wordsConfig.GetNamedArray("threat_checks").GetObjectAt(0).GetNamedArray("expressions");

                        // Now, populate all relevant variables
                        CAN_CHANGE_VOTE = (bool)Helper.GetJSONValue(votingConfig, "enableVoteChanging", ExpectedReturnType.BOOLEAN, true);
                        Debug.WriteLine("YikYak.API.UpdateConfig: CAN_CHANGE_VOTE is {0}", CAN_CHANGE_VOTE);

                        DANGER_WORDS_MESSAGE = (String)Helper.GetJSONValue(threatConfig, "message", ExpectedReturnType.STRING, DANGER_WORDS_MESSAGE);
                        Settings.DangerWords_Message = DANGER_WORDS_MESSAGE;
                        Debug.WriteLine("YikYak.API.UpdateConfig: DangerWordsMessage is \"{0}\"", DANGER_WORDS_MESSAGE);

                        DANGER_WORDS = new String[expressions.Count];
                        for (uint i = 0; i < expressions.Count; i++)
                            DANGER_WORDS[i] = expressions.GetStringAt(i);

                        Settings.DangerWords_Array = DANGER_WORDS;
                        Debug.WriteLine("YikYak.API.UpdateConfig: DANGER_WORDS is [{0}]", String.Join(",", DANGER_WORDS));

                        return new Response<bool>() { Result = Result.SUCCESS, Return = true };
                    }
                    else
                        return new Response<bool>() { Result = resp2.Result, Return = false };
                }
                catch
                {
                    return new Response<bool>() { Result = Result.INTERNAL_API_FAILURE, Return = false };
                } 
            }
            else
                return new Response<bool>() { Result = resp1.Result, Return = false };
        }

        #endregion

        #region Public API Calls

        async public Task<Response<bool>> LogEvent(LogEventType type)
        {
            Uri dest = Helper.BuildURI(API_URI, "logEvent", Location.Accuracy, null, null, null, null, UserID, Location.Latitude, Location.Longitude, null, null, null, null);

            List<String> fields = new List<String>();
            List<String> values = new List<String>();

            if (type == LogEventType.ApplicationDidEnterBackground)
            {
                fields.Add("Duration");
                values.Add(((int)(DateTime.Now - DATETIME_API_STARTED).TotalSeconds).ToString());
            }
            else
            {
                fields.Add("Source");
                values.Add("Direct");
            }

            fields.AddRange(new String[] { "accuracy", "eventType", "hash", "lat", "long", "salt", "userID", "userLat", "userLong", "version" });

            values.Add(Location.Accuracy.ToString("F1"));
            if (type == LogEventType.ApplicationDidBecomeActive) values.Add("ApplicationDidBecomeActive");
            else values.Add("ApplicationDidEnterBackground");
            values.Add(dest.Query.Substring(dest.Query.LastIndexOf('=') + 1)); // Hash value must match that from the URI
            values.Add(Location.Latitude.ToString("F7"));
            values.Add(Location.Longitude.ToString("F7"));

            int idxSaltStart = dest.Query.LastIndexOf("salt=") + 5;
            int saltLength = dest.Query.IndexOf('&', idxSaltStart) - idxSaltStart;
            values.Add(dest.Query.Substring(idxSaltStart, saltLength));

            values.Add(UserID);
            values.Add(Location.Latitude.ToString("F7"));
            values.Add(Location.Longitude.ToString("F7"));
            values.Add(Config.API_VERSION);

            HttpStringContent content = Helper.BuildPOSTContent(fields.ToArray(), values.ToArray());
            Response<String> resp = await Helper.Post(HTTP_CLIENT, dest, content, CancellationToken.None);

            if (resp.Result == Result.SUCCESS && resp.Return.Equals("1"))
                return new Response<bool>() { Result = Result.SUCCESS, Return = true };
            else
                return new Response<bool>() { Result = resp.Result, Return = false };
        }

        async public Task<Response<Notification[]>> GetNotifications()
        {
            Response<String> resp = await Helper.Get(HTTP_CLIENT, new Uri(Config.NOTIFY_URI, "getAllForUser/" + UserID), CancellationToken.None);
            JsonObject respObj = null;

            if (resp.Result == Result.SUCCESS)
            {
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    String status = (String)Helper.GetJSONValue(respObj, "status", ExpectedReturnType.STRING, "");
                    if (status.Equals("ok") && respObj.ContainsKey("data"))
                    {
                        JsonArray dataArr = respObj.GetNamedArray("data");

                        if (dataArr.Count > 0)
                        {
                            Notification[] notifs = new Notification[dataArr.Count];
                            UnreadNotifications = 0;

                            for (uint i = 0; i < dataArr.Count; i++)
                            {
                                JsonObject currObj = dataArr.GetObjectAt(i);
                                notifs[i] = Notification.Create(currObj);

                                if (notifs[i] != null && !notifs[i].IsRead)
                                    UnreadNotifications++;
                            }

                            return new Response<Notification[]>() { Result = Result.SUCCESS, Return = notifs };
                        }
                    }
                }
            }
            else
            {
                return new Response<Notification[]>() { Result = resp.Result, Return = null };
            }

            return new Response<Notification[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
        }

        async public Task<Response<bool>> MarkNotificationsRead(Notification[] notifs)
        {
            if (notifs != null && notifs.Length > 0)
            {
                JsonObject postObj = new JsonObject();

                JsonArray notifIDArray = new JsonArray();
                foreach (Notification n in notifs)
                {
                    notifIDArray.Add(JsonValue.CreateStringValue(n.ID));
                }
                postObj.Add("notificationIDs", notifIDArray);

                postObj.Add("userID", JsonValue.CreateStringValue(UserID));
                postObj.Add("status", JsonValue.CreateStringValue("read"));

                HttpStringContent content = new HttpStringContent(postObj.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                Response<String> resp = await Helper.Post(HTTP_CLIENT, new Uri(Config.NOTIFY_URI, "updateBatch/"), content, CancellationToken.None);

                if (resp.Result == Result.SUCCESS)
                {
                    JsonObject respObj = null;
                    if (JsonObject.TryParse(resp.Return, out respObj))
                    {
                        String status = (String)Helper.GetJSONValue(respObj, "status", ExpectedReturnType.STRING, "");
                        if (status.Equals("ok"))
                        {
                            foreach (Notification n in notifs)
                            {
                                n.IsRead = true;
                            }

                            UnreadNotifications -= notifs.Length;

                            return new Response<bool>() { Result = Result.SUCCESS, Return = true };
                        }
                    }
                    
                    return new Response<bool>() { Result = Result.INTERNAL_API_FAILURE, Return = false };
                }
                else
                {
                    return new Response<bool>() { Result = resp.Result, Return = false };
                }
            }

            return new Response<bool>() { Result = Result.BAD_PARAMETERS, Return = false };
        }



        async public Task<Response<Yak>> GetYak(String yakID)
        {
            Uri req = Helper.BuildURI(API_URI, "getMessage", Location.Accuracy, null, null, null,
                                      null, UserID, Location.Latitude, Location.Longitude, yakID, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                        return new Response<Yak>() { Result = Result.SUCCESS, Return = yaks[0] };
                }

                return new Response<Yak>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetLocalRecent()
        {
            Uri req = Helper.BuildURI(API_URI, "getMessages", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yakarma = (int)Helper.GetJSONValue(respObj, "yakarma", ExpectedReturnType.INT, 0);

                    if (respObj.ContainsKey("otherLocations"))
                    {
                        JsonArray locations = respObj.GetNamedArray("otherLocations");
                        PeekLocations = new PeekLocation[locations.Count];

                        for (uint i = 0; i < PeekLocations.Length; i++)
                        {
                            PeekLocations[i] = PeekLocation.Create(locations.GetObjectAt(i), false);
                        }
                    }

                    if (respObj.ContainsKey("featuredLocations"))
                    {
                        JsonArray locations = respObj.GetNamedArray("featuredLocations");
                        FeaturedLocations = new PeekLocation[locations.Count];

                        for (uint i = 0; i < FeaturedLocations.Length; i++)
                        {
                            FeaturedLocations[i] = PeekLocation.Create(locations.GetObjectAt(i), true);
                        }
                    }

                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetLocalHot()
        {
            Uri req = Helper.BuildURI(API_URI, "hot", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetPeekYaks(PeekLocation peek)
        {
            Uri req = Helper.BuildURI(API_URI, "getPeekMessages", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, peek.ID, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                    {
                        // Set Permissions
                        foreach (Yak y in yaks)
                        {
                            if (!peek.CanVote) y.Vote = VoteStatus.VOTING_DISABLED;
                            if (!peek.CanReply) y.IsReadOnly = true;
                            if (!peek.CanReport) y.CanReport = false;
                        }

                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                    }
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetCustomPeekYaks(BaseLocation peek, bool hot)
        {
            String dest = (hot) ? "hot" : "yaks";

            Uri req = Helper.BuildURI(API_URI, dest, Location.Accuracy, null, null, peek.Latitude,
                                      peek.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                    {
                        // Set Permissions
                        foreach (Yak y in yaks)
                        {
                            y.Vote = VoteStatus.VOTING_DISABLED;
                            y.IsReadOnly = true;
                            y.CanReport = false;
                        }

                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                    }
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetYaksAtLocation(double? latitude, double? longitude)
        {
            if (latitude == null) latitude = Location.Latitude;
            if (longitude == null) longitude = Location.Longitude;

            Uri req = Helper.BuildURI(API_URI, "getMessages", Location.Accuracy, null, null, latitude,
                                      longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                    {
                        // Set Permissions
                        foreach (Yak y in yaks)
                        {
                            y.Vote = VoteStatus.VOTING_DISABLED;
                            y.IsReadOnly = true;
                            y.CanReport = false;
                        }

                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                    }
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetMyYaks()
        {
            Uri req = Helper.BuildURI(API_URI, "getMyRecentYaks", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                    {
                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                    }
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Yak[]>> GetMyReplies()
        {
            Uri req = Helper.BuildURI(API_URI, "getMyRecentReplies", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, null, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    Yak[] yaks = Helper.GetYaksFromJSON(respObj);
                    if (yaks != null && yaks.Length > 0)
                    {
                        return new Response<Yak[]>() { Result = Result.SUCCESS, Return = yaks };
                    }
                }

                return new Response<Yak[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Yak[]>() { Result = resp.Result, Return = null };
            }
        }

        async public Task<Response<Comment[]>> GetComments(Yak yak)
        {
            Uri req = Helper.BuildURI(API_URI, "getComments", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, yak.ID, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);
            if (resp.Result == Result.SUCCESS)
            {
                JsonObject respObj = null;
                if (JsonObject.TryParse(resp.Return, out respObj))
                {
                    if (respObj.ContainsKey("comments"))
                    {
                        JsonArray jsonComments = respObj.GetNamedArray("comments");
                        List<Comment> listComments = new List<Comment>();

                        for (uint i = 0; i < jsonComments.Count; i++)
                        {
                            Comment c = Comment.Create(yak, jsonComments.GetObjectAt(i));
                            if (c != null) listComments.Add(c);
                        }

                        return new Response<Comment[]>() { Result = Result.SUCCESS, Return = listComments.ToArray() };
                    }
                }

                return new Response<Comment[]>() { Result = Result.INTERNAL_API_FAILURE, Return = null };
            }
            else
            {
                return new Response<Comment[]>() { Result = resp.Result, Return = null };
            }
        }



        async public Task<Response<bool>> VoteOnYak(Yak yak, bool isUpvote)
        {
            if (yak.Vote != VoteStatus.VOTING_DISABLED || (!CAN_CHANGE_VOTE && yak.Vote != VoteStatus.NO_VOTE))
            {
                yak.DoVote(isUpvote);

                String dest = isUpvote ? "likeMessage" : "downvoteMessage";
                Uri req = Helper.BuildURI(API_URI, dest, Location.Accuracy, null, null, null, null, UserID, Location.Latitude, Location.Longitude, yak.ID, null, null, null);
                Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);

                if (resp.Result == Result.SUCCESS)
                    return new Response<bool>() { Result = Result.SUCCESS, Return = true };

                yak.DoVote(isUpvote); // "Undo" the Vote
                return new Response<bool>() { Result = resp.Result, Return = false };
            }
            else
            {
                return new Response<bool>() { Result = Result.INVALID_PERMISSIONS, Return = false };
            }
        }

        async public Task<Response<bool>> VoteOnComment(Comment comment, bool isUpvote)
        {
            if (comment.Vote != VoteStatus.VOTING_DISABLED || (!CAN_CHANGE_VOTE && comment.Vote != VoteStatus.NO_VOTE))
            {
                comment.DoVote(isUpvote);

                String dest = isUpvote ? "likeComment" : "downvoteComment";
                String messageID = isUpvote ? null : comment.Yak.ID;
                Uri req = Helper.BuildURI(API_URI, dest, Location.Accuracy, null, null, null, null, UserID, Location.Latitude, Location.Longitude, messageID, comment.ID, null, null);
                Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);

                if (resp.Result != Result.SUCCESS)
                {
                    comment.DoVote(isUpvote);
                    return new Response<bool>() { Result = resp.Result, Return = false };
                }

                return new Response<bool>() { Result = Result.SUCCESS, Return = true };
            }
            else
            {
                return new Response<bool>() { Result = Result.INVALID_PERMISSIONS, Return = false };
            }
        }

        async public Task<Response<bool>> ReportYak(Yak yak, String reason)
        {
            Uri req = Helper.BuildURI(API_URI, "reportMessage", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, yak.ID, null, null, reason);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);

            if (resp.Result == Result.SUCCESS)
                return new Response<bool>() { Result = Result.SUCCESS, Return = true };
            else
                return new Response<bool>() { Result = resp.Result, Return = false };

        }

        async public Task<Response<bool>> DeleteYak(Yak yak)
        {
            Uri req = Helper.BuildURI(API_URI, "deleteMessage2", Location.Accuracy, null, null, Location.Latitude,
                                      Location.Longitude, UserID, Location.Latitude, Location.Longitude, yak.ID, null, null, null);
            Response<String> resp = await Helper.Get(HTTP_CLIENT, req, CancellationToken.None);

            if (resp.Result == Result.SUCCESS)
                return new Response<bool>() { Result = Result.SUCCESS, Return = true };
            else
                return new Response<bool>() { Result = resp.Result, Return = false };

        }



        public bool HasDangerousWording(String message)
        {
            if (DANGER_WORDS == null || DANGER_WORDS.Length == 0) return false;

            foreach (String regex in DANGER_WORDS)
            {
                try
                {
                    if (Regex.IsMatch(message, regex, RegexOptions.None))
                        return true;
                }
                catch
                {
                    // Probably a unicode character ... so deal with it
                    String unicodeSymbol = char.ConvertFromUtf32(int.Parse(regex.Substring(2), System.Globalization.NumberStyles.HexNumber)).ToString();
                    if (message.Contains(unicodeSymbol))
                        return true;
                }
            }

            return false;
        }

        async public Task<Response<bool>> PostYak(String message, String handle, bool bypassedThreatPopup)
        {
            if (POST_ACCESS)
            {
                if (!String.IsNullOrWhiteSpace(message) && message.Length <= 200 && (String.IsNullOrWhiteSpace(handle) || handle.Length <= 15))
                {
                    Uri req = Helper.BuildURI(API_URI, "sendMessage", null, null, null, null, null, UserID, null, null, null, null, null, null);

                    int idxSaltStart = req.Query.LastIndexOf("salt=") + 5;
                    int saltLength = req.Query.IndexOf('&', idxSaltStart) - idxSaltStart;

                    String[] fields = { "bypassedThreatPopup", "hash", (!String.IsNullOrWhiteSpace(handle) ? "hndl" : null), "lat", "long", "message", "salt", "userID", "version" };
                    String[] values = { (bypassedThreatPopup ? "1" : "0"), req.Query.Substring(req.Query.LastIndexOf('=')+1), handle, Location.Latitude.ToString("F7"), Location.Longitude.ToString("F7"), message, req.Query.Substring(idxSaltStart, saltLength), UserID, Config.API_VERSION };
                    HttpStringContent content = Helper.BuildPOSTContent(fields, values);

                    Response<String> resp = await Helper.Post(HTTP_CLIENT, req, content, CancellationToken.None);

                    if (resp.Result == Result.SUCCESS && resp.Return.Equals("1"))
                        return new Response<bool>() { Result = Result.SUCCESS, Return = true };
                    else
                        return new Response<bool>() { Result = resp.Result, Return = false };
                }

                return new Response<bool>() { Result = Result.BAD_PARAMETERS, Return = false };
            }
            else
            {
                return new Response<bool>() { Result = Result.INVALID_PERMISSIONS, Return = false };
            }

        }

        async public Task<Response<bool>> PostComment(Yak yak, String comment, bool bypassedThreatPopup)
        {
            if (POST_ACCESS)
            {
                if (!String.IsNullOrWhiteSpace(comment) && comment.Length <= 200)
                {
                    Uri req = Helper.BuildURI(API_URI, "postComment", Location.Accuracy, null, null, null, null, UserID, Location.Latitude, Location.Longitude, null, null, null, null);

                    int idxSaltStart = req.Query.LastIndexOf("salt=") + 5;
                    int saltLength = req.Query.IndexOf('&', idxSaltStart) - idxSaltStart;

                    String[] fields = { "accuracy", "bypassedThreatPopup", "comment", "hash",  "lat", "long", "messageID", "salt", "userID", "userLat", "userLong", "version" };
                    String[] values = { Location.Accuracy.ToString("F1"), (bypassedThreatPopup ? "1" : "0"), comment, req.Query.Substring(req.Query.LastIndexOf('=') + 1), Location.Latitude.ToString("F7"), Location.Longitude.ToString("F7"), yak.ID, req.Query.Substring(idxSaltStart, saltLength), UserID, Location.Latitude.ToString("F7"), Location.Longitude.ToString("F7"), Config.API_VERSION };
                    HttpStringContent content = Helper.BuildPOSTContent(fields, values);

                    Response<String> resp = await Helper.Post(HTTP_CLIENT, req, content, CancellationToken.None);

                    if (resp.Result == Result.SUCCESS && resp.Return.Equals("1"))
                        return new Response<bool>() { Result = Result.SUCCESS, Return = true };
                    else
                        return new Response<bool>() { Result = resp.Result, Return = false };
                }

                return new Response<bool>() { Result = Result.BAD_PARAMETERS, Return = false };
            }
            else
            {
                return new Response<bool>() { Result = Result.INVALID_PERMISSIONS, Return = false };
            }
        }

        async public Task<Response<bool>> PostPeekYak(PeekLocation peek, String message, String handle, bool bypassedThreatPopup)
        {
            if (POST_ACCESS && peek.CanSubmit)
            {
                if (!String.IsNullOrWhiteSpace(message) && message.Length <= 200 && (String.IsNullOrWhiteSpace(handle) || handle.Length <= 15))
                {
                    Uri req = Helper.BuildURI(API_URI, "submitPeekMessage", null, null, null, null, null, UserID, null, null, null, null, null, null);

                    int idxSaltStart = req.Query.LastIndexOf("salt=") + 5;
                    int saltLength = req.Query.IndexOf('&', idxSaltStart) - idxSaltStart;

                    String[] fields = { "bypassedThreatPopup", "hash", (!String.IsNullOrWhiteSpace(handle) ? "hndl" : null), "message", "peekID", "salt", "userID", "version" };
                    String[] values = { (bypassedThreatPopup ? "1" : "0"), req.Query.Substring(req.Query.LastIndexOf('=') + 1), handle, message, peek.ID.ToString(), req.Query.Substring(idxSaltStart, saltLength), UserID, Config.API_VERSION };
                    HttpStringContent content = Helper.BuildPOSTContent(fields, values);

                    Response<String> resp = await Helper.Post(HTTP_CLIENT, req, content, CancellationToken.None);

                    if (resp.Result == Result.SUCCESS)
                        return new Response<bool>() { Result = Result.SUCCESS, Return = true };
                    else
                        return new Response<bool>() { Result = resp.Result, Return = false };
                }

                return new Response<bool>() { Result = Result.BAD_PARAMETERS, Return = false };
            }
            else
            {
                return new Response<bool>() { Result = Result.INVALID_PERMISSIONS, Return = false };
            }
        }

        #endregion

        #region Non-API Calls

        public void EnableTravelMode()
        {
            Settings.TravelMode_Enabled = true;
        }

        async public Task DisableTravelMode()
        {
            Settings.TravelMode_Enabled = false;

            Location = await Helper.GetLocation(500, TimeSpan.Zero);
        }

        #endregion
    }
}
