using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YikYak
{
    /// <summary>
    /// Vital encryption keys required for the proper functionality of the API.
    /// 
    /// NOTE: I will not share these publicly!  If you are interested in also using
    /// this API, you must find them out yourself.  Best of luck.
    /// </summary>
    internal class Config
    {
        /// <summary>
        /// The Encryption Key required for creating valid requests to the YikYak API
        /// </summary>
        public const String URI_ENCRYPTION_KEY = "";

        /// <summary>
        /// The Encryption Key required for creating valid OAuth headers to register with the Parse API hosting service
        /// </summary>
        public const String PARSE_ENCRYPTION_KEY = "";

        /// <summary>
        /// The Consumer ID required for creating valid OAuth headers to register with the Parse API hosting service
        /// </summary>
        public const String PARSE_CONSUMER_ID = "";


        public const String API_VERSION = "2.4.2";

        /// <summary>
        /// The User-Agent to provide when making requests.  For best results, select a correctly formatted, fairly common Android Dalvik user agent.
        /// </summary>
        public const String USER_AGENT = "<USER-AGENT>" + " " + API_VERSION;

        public static readonly Uri CLOUDFRONT_URI = new Uri("https://d3436qb9f9xu23.cloudfront.net/");
        public static readonly Uri NOTIFY_URI = new Uri("https://notify.yikyakapi.net/api/");
        public static readonly Uri PARSE_URI = new Uri("https://api.parse.com/2/");
    }
}
