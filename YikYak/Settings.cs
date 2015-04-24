using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace YikYak
{
    public class Settings
    {
        internal static class Keys
        {
            internal const String SavedLocation_Latitude = "SettingsStashedLatitude";
            internal const String SavedLocation_Longitude = "SettingsStashedLongitude";
            internal const String SavedLocation_Accuracy = "SettingsStashedAccuracy";
            internal const String SavedLocation_Timestamp = "SettingsStashedTimestamp";
            internal const String TravelMode_Enabled = "SettingsIsTravelModeEnabled";

            internal const String UserID = "YikYakUserID";
            internal const String UserID_ForceNew = "YikYakUserIDForceNew";
            internal const String UserID_GenerationMethod = "YikYakUserIDGenMethod";

            internal const String API_BaseUrl = "YikYakAPIBaseURL";
            internal const String ThreatChecksList = "YikYakThreatChecksList";
            internal const String ThreatChecksBase = "YikYakThreatCheckNumber";

            internal const String Parse_Success = "ParseRegistrationSuccess";
            internal const String Parse_IID = "ParseInstallationID";
            internal const String Parse_OID = "ParseObjectID";

            internal const String CustomPeekLocations = "YikYakCustomLocations";

            internal const String SuppressYakAds = "YikYakSuppressYakAds";
        }

        public static double? SavedLocation_Latitude
        {
            get { return (double?)TryGetLocalSetting(Keys.SavedLocation_Latitude); }
            internal set { PutLocalSetting(Keys.SavedLocation_Latitude, value); }
        }

        public static double? SavedLocation_Longitude
        {
            get { return (double?)TryGetLocalSetting(Keys.SavedLocation_Longitude); }
            internal set { PutLocalSetting(Keys.SavedLocation_Longitude, value); }
        }

        public static double? SavedLocation_Accuracy
        {
            get { return (double?)TryGetLocalSetting(Keys.SavedLocation_Accuracy); }
            internal set { PutLocalSetting(Keys.SavedLocation_Accuracy, value); }
        }

        public static DateTime SavedLocation_Timestamp
        {
            get 
            {
                DateTime tmp = DateTime.MinValue;
                DateTime.TryParse((String)TryGetLocalSetting(Keys.SavedLocation_Timestamp, ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out tmp);
                return tmp;
            }
            internal set { PutLocalSetting(Keys.SavedLocation_Timestamp, value.ToUniversalTime().ToString()); }
        }

        public static bool TravelMode_Enabled
        {
            get { return (bool)TryGetLocalSetting(Keys.TravelMode_Enabled, false); }
            set { PutLocalSetting(Keys.TravelMode_Enabled, value); }
        }

        public static String UserID
        {
            get { return (String)TryGetLocalSetting(Keys.UserID); }
            internal set { PutLocalSetting(Keys.UserID, value); }
        }

        public static bool UserID_ForceNew
        {
            get { return (bool)TryGetLocalSetting(Keys.UserID_ForceNew, false); }
            set { PutLocalSetting(Keys.UserID_ForceNew, value); }
        }

        public static UserIDGenerationMethod UserID_GenerationMethod
        {
            get { return (UserIDGenerationMethod)TryGetLocalSetting(Keys.UserID_GenerationMethod, UserIDGenerationMethod.Standard); }
            set { PutLocalSetting(Keys.UserID_GenerationMethod, (int)value); }
        }

        public static String API_BaseUrl
        {
            get { return (String)TryGetLocalSetting(Keys.API_BaseUrl, "https://us-central-api.yikyakapi.net/api/"); }
            internal set { PutLocalSetting(Keys.API_BaseUrl, value); }
        }

        public static String[] ThreatChecksList
        {
            get { return (String[])TryGetLocalSetting(Keys.ThreatChecksList); }
            internal set { PutLocalSetting(Keys.ThreatChecksList, value); }
        }

        public static bool Parse_Success
        {
            get { return (bool)TryGetLocalSetting(Keys.Parse_Success, false); }
            internal set { PutLocalSetting(Keys.Parse_Success, value); }
        }

        public static Guid Parse_IID
        {
            get { return (Guid)TryGetLocalSetting(Keys.Parse_IID, Guid.Empty); }
            internal set { PutLocalSetting(Keys.Parse_IID, value); }
        }

        public static String Parse_OID
        {
            get { return (String)TryGetLocalSetting(Keys.Parse_OID); }
            internal set { PutLocalSetting(Keys.Parse_OID, value); }
        }

        public static BaseLocation[] CustomPeekLocations
        {
            get { return BaseLocation.Deserialize((String)TryGetLocalSetting(Keys.CustomPeekLocations)); }
            internal set { PutLocalSetting(Keys.CustomPeekLocations, BaseLocation.Serialize(value)); }
        }

        public static bool SuppressYakAds
        {
            get { return (bool)TryGetLocalSetting(Keys.SuppressYakAds, false); }
            internal set { PutLocalSetting(Keys.SuppressYakAds, value); }
        }

        internal static Object TryGetLocalSetting(String key, Object def = null)
        {
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey(key))
                return ApplicationData.Current.LocalSettings.Values[key];

            return def;
        }

        internal static void PutLocalSetting(String key, Object val)
        {
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey(key))
                ApplicationData.Current.LocalSettings.Values[key] = val;
            else
                ApplicationData.Current.LocalSettings.Values.Add(key, val);
        }

        
    }
}
