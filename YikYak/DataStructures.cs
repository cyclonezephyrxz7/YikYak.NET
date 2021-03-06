﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Geolocation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace YikYak
{
    #region Supporting Data Structures

    public enum VoteStatus
    {
        LIKED,
        NO_VOTE,
        DISLIKED,
        VOTING_DISABLED
    }

    public enum LogEventType
    {
        ApplicationDidBecomeActive,
        ApplicationDidEnterBackground
    }

    internal enum ExpectedReturnType
    {
        STRING,
        INT,
        DOUBLE,
        DATETIME,
        BOOLEAN,
        VOTESTATUS
    }

    public enum Result
    {
        SUCCESS,
        WEB_REQUEST_FAILED,
        WEB_REQUEST_TIMED_OUT,
        INVALID_PERMISSIONS,
        BAD_PARAMETERS,
        INTERNAL_API_FAILURE
    }

    public struct Response<T>
    {
        public Result Result;
        public T Return;
    }

    public enum UserIDGenerationMethod
    {
        Standard, // Uses the ASHWID
        Reversed, // Reverses the ASHWID bytes
        Doubled, // Concatenates the ASHWID bytes to itself, doubling it's size
        Palindrome // Concatenates the reverse of the ASHWID bytes to itself, creating a Palindrome
    }

    #endregion

    #region Primary Data Structures

    public class YakObject : INotifyPropertyChanged, IComparable
    {
        public String ID { get; set; }
        public String PosterID { get; set; }
        public String Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsMine { 
            get 
            {
                if (API.Instance != null)
                    return API.Instance.UserID.Equals(PosterID);
                else
                    return false;
            } 
        }

        private int _score;
        public int Score
        {
            get { return _score; }

            set
            {
                _score = value;
                OnPropertyChanged("Score");
            }
        }

        private VoteStatus _vote;
        public VoteStatus Vote
        {
            get { return _vote; }

            set
            {
                _vote = value;
                OnPropertyChanged("Vote");
            }
        }

        public int DeliveryID { get; set; } // Probably to be used for sorting

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName)
        {
            if (this.PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }

        public int CompareTo(object obj)
        {
            if (obj.GetType() == this.GetType())
            {
                return (((YakObject)obj)._score).CompareTo(_score); // By doing it this way, it is 'reversed' (DESCENDING)
            }
            else
            {
                return 0;
            }
        }

        public void DoVote(bool isUpvote)
        {
            if (isUpvote)
            {
                if (this.Vote == VoteStatus.NO_VOTE)
                {
                    this.Score += 1;
                    this.Vote = VoteStatus.LIKED;
                }
                else if (this.Vote == VoteStatus.DISLIKED)
                {
                    this.Score += 2;
                    this.Vote = VoteStatus.LIKED;
                }
                else // was LIKED
                {
                    this.Score -= 1;
                    this.Vote = VoteStatus.NO_VOTE;
                }
            }
            else // !isUpvote
            {
                if (this.Vote == VoteStatus.NO_VOTE)
                {
                    this.Score -= 1;
                    this.Vote = VoteStatus.DISLIKED;
                }
                else if (this.Vote == VoteStatus.DISLIKED)
                {
                    this.Score += 1;
                    this.Vote = VoteStatus.NO_VOTE;
                }
                else // was LIKED
                {
                    this.Score -= 2;
                    this.Vote = VoteStatus.DISLIKED;
                }
            }
        }
    }

    public class Comment : YakObject
    {
        public Yak Yak { get; private set; }

        public String OverlayID { get; private set; }
        public String BackgroundID { get; private set; }

        public BitmapImage OverlayImage { get; private set; }
        public BitmapImage BackgroundImage { get; private set; }

        private int _waitCounter;
        /// <summary>
        /// Creates a Comment from a provided JsonObject.  For missing data, fields will assume default values.
        /// If critical fields are missing data, the function will fail.
        /// </summary>
        /// <param name="input">The JsonObject containing the information to build this class.</param>
        /// <returns>A Comment based on input, or null if the input is bad.</returns>
        public static Comment Create(Yak yak, JsonObject input)
        {
            if (input.ContainsKey("commentID") && input.ContainsKey("comment"))
            {
                Comment c = new Comment();

                c.Yak = yak;
                c.ID = (String)Helper.GetJSONValue(input, "commentID", ExpectedReturnType.STRING, "");
                c.Content = (String)Helper.GetJSONValue(input, "comment", ExpectedReturnType.STRING, "");
                if (c.Yak == null || String.IsNullOrWhiteSpace(c.ID) || String.IsNullOrWhiteSpace(c.Content))
                {
                    return null;
                }

                c.PosterID = (String)Helper.GetJSONValue(input, "posterID", ExpectedReturnType.STRING, "");
                //c.Timestamp = (DateTime)Helper.GetJSONValue(input, "time", ExpectedReturnType.DATETIME, new DateTime());
                long epoch = (long)((double)Helper.GetJSONValue(input, "gmt", ExpectedReturnType.DOUBLE, -1.0));
                if (epoch == -1.0)
                    c.Timestamp = DateTime.Now;
                else
                    c.Timestamp = (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddSeconds(epoch).ToLocalTime();

                c.Score = (int)Helper.GetJSONValue(input, "numberOfLikes", ExpectedReturnType.INT, int.MinValue);
                c.Vote = (yak.Vote == VoteStatus.VOTING_DISABLED) ? VoteStatus.VOTING_DISABLED : (VoteStatus)Helper.GetJSONValue(input, "liked", ExpectedReturnType.VOTESTATUS, VoteStatus.NO_VOTE);

                c.OverlayID = (String)Helper.GetJSONValue(input, "overlayID", ExpectedReturnType.STRING, null);
                c.BackgroundID = (String)Helper.GetJSONValue(input, "backID", ExpectedReturnType.STRING, null);

                int overlayInt;
                if (!String.IsNullOrWhiteSpace(c.OverlayID) && int.TryParse(c.OverlayID, out overlayInt) && overlayInt >= 0 && overlayInt <= 20)
                    c.OverlayImage = new BitmapImage(new Uri("ms-appx:///YikYak/Foregrounds/" + c.OverlayID + ".png"));
                else
                    c.OverlayImage = null;

                int backgroundInt;
                if (!String.IsNullOrWhiteSpace(c.BackgroundID) && int.TryParse(c.BackgroundID, out backgroundInt) && backgroundInt >= 0 && backgroundInt <= 10)
                    c.BackgroundImage = new BitmapImage(new Uri("ms-appx:///YikYak/Backgrounds/" + c.BackgroundID + ".png"));
                else
                    c.BackgroundImage = null;

                c.DeliveryID = (int)Helper.GetJSONValue(input, "deliveryID", ExpectedReturnType.INT, 0);

                return c;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// In the event that I haven't got a local cached image for either of the Background or Overlay,
        /// this method will retrieve them asynchronously from the Cloudfront hosting service.
        /// </summary>
        async public Task<bool> GetBackgroundAndOverlay()
        {
            if (_waitCounter != 0)
                return false;

            int tmp;
            if (!String.IsNullOrWhiteSpace(OverlayID) && !String.IsNullOrWhiteSpace(BackgroundID) &&
                int.TryParse(OverlayID, out tmp) && int.TryParse(BackgroundID, out tmp))
            {
                try
                {
                    OverlayImage = new BitmapImage(new Uri(Config.CLOUDFRONT_URI, "/replier/overlays/android/drawable-xxhdpi/" + OverlayID));
                    Interlocked.Increment(ref _waitCounter);

                    BackgroundImage = new BitmapImage(new Uri(Config.CLOUDFRONT_URI, "/replier/backgrounds/android/drawable-xxhdpi/" + BackgroundID));
                    Interlocked.Increment(ref _waitCounter);

                    OverlayImage.ImageOpened += OverlayImage_ImageOpened;
                    OverlayImage.ImageFailed += OverlayImage_ImageFailed;

                    BackgroundImage.ImageOpened += BackgroundImage_ImageOpened;
                    BackgroundImage.ImageFailed += BackgroundImage_ImageFailed;

                    await Task.Run(() =>
                    {
                        while (_waitCounter > 0) { };
                    });

                    return (BackgroundImage != null && OverlayImage != null);
                }
                catch
                {
                    OverlayImage = null;
                    BackgroundImage = null;
                }
            }

            return false;
        }

        private void BackgroundImage_ImageFailed(object sender, Windows.UI.Xaml.ExceptionRoutedEventArgs e)
        {
            BackgroundImage = null;
            Interlocked.Decrement(ref _waitCounter);
        }

        private void BackgroundImage_ImageOpened(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Interlocked.Decrement(ref _waitCounter);
        }

        private void OverlayImage_ImageFailed(object sender, Windows.UI.Xaml.ExceptionRoutedEventArgs e)
        {
            OverlayImage = null;
            Interlocked.Decrement(ref _waitCounter);
        }

        private void OverlayImage_ImageOpened(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Interlocked.Decrement(ref _waitCounter);
        }
    }

    public class Yak : YakObject
    {
        public String PosterHandle { get; set; }
        public int CommentsCount
        {
            get
            {
                return _commentsCount;
            }

            set
            {
                _commentsCount = value;
                OnPropertyChanged("CommentsCount");
            }
        }
        private int _commentsCount = 0;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool HidePin { get; set; }
        public bool IsReYaked { get; set; }
        public bool IsReadOnly { get; set; }
        public bool CanReport { get; set; }

        /// <summary>
        /// Creates a Yak from a provided JsonObject.  For missing data, fields will assume default values.
        /// If critical fields are missing data, the function will fail.
        /// </summary>
        /// <param name="input">The JsonObject containing the information to build this class.</param>
        /// <returns>A Yak based on input, or null if the input is bad.</returns>
        public static Yak Create(JsonObject input)
        {
            if (input.ContainsKey("messageID") && input.ContainsKey("message"))
            {
                Yak y = new Yak();

                y.ID = (String)Helper.GetJSONValue(input, "messageID", ExpectedReturnType.STRING, "");
                y.Content = (String)Helper.GetJSONValue(input, "message", ExpectedReturnType.STRING, "");
                if (String.IsNullOrWhiteSpace(y.ID) || String.IsNullOrWhiteSpace(y.Content))
                {
                    return null;
                }

                y.PosterHandle = (String)Helper.GetJSONValue(input, "handle", ExpectedReturnType.STRING, "");
                y.PosterID = (String)Helper.GetJSONValue(input, "posterID", ExpectedReturnType.STRING, "");
                //y.Timestamp = (DateTime)Helper.GetJSONValue(input, "time", ExpectedReturnType.DATETIME, new DateTime());
                long epoch = (long)((double)Helper.GetJSONValue(input, "gmt", ExpectedReturnType.DOUBLE, -1.0));
                if (epoch == -1.0)
                    y.Timestamp = DateTime.Now;
                else
                    y.Timestamp = (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddSeconds(epoch).ToLocalTime();

                y.CommentsCount = (int)Helper.GetJSONValue(input, "comments", ExpectedReturnType.INT, 0);
                y.Score = (int)Helper.GetJSONValue(input, "numberOfLikes", ExpectedReturnType.INT, 0);
                y.Vote = (VoteStatus)Helper.GetJSONValue(input, "liked", ExpectedReturnType.VOTESTATUS, VoteStatus.NO_VOTE);
                y.HidePin = (bool)Helper.GetJSONValue(input, "hidePin", ExpectedReturnType.BOOLEAN, true);
                y.IsReYaked = (bool)Helper.GetJSONValue(input, "reyaked", ExpectedReturnType.BOOLEAN, false);
                y.IsReadOnly = (bool)Helper.GetJSONValue(input, "readOnly", ExpectedReturnType.BOOLEAN, false);
                y.CanReport = true; // By default, this value set by PeekLocation

                y.Latitude = (double)Helper.GetJSONValue(input, "latitude", ExpectedReturnType.DOUBLE, double.MinValue);
                y.Longitude = (double)Helper.GetJSONValue(input, "longitude", ExpectedReturnType.DOUBLE, double.MinValue);

                y.DeliveryID = (int)Helper.GetJSONValue(input, "deliveryID", ExpectedReturnType.INT, 0);

                return y;
            }
            else
            {
                return null;
            }
        }
    }

    public class BaseLocation
    {
        public String Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanVote { get; set; }
        public bool CanReply { get; set; }
        public bool CanReport { get; set; }

        public static String Serialize(BaseLocation[] locs)
        {
            JsonObject jObj = new JsonObject();
            JsonArray jArr = new JsonArray();

            foreach (BaseLocation loc in locs)
            {
                JsonObject cObj = new JsonObject();
                cObj.Add("Name", JsonValue.CreateStringValue(loc.Name));
                cObj.Add("Latitude", JsonValue.CreateNumberValue(loc.Latitude));
                cObj.Add("Longitude", JsonValue.CreateNumberValue(loc.Longitude));
                cObj.Add("CanSubmit", JsonValue.CreateBooleanValue(loc.CanSubmit));
                cObj.Add("CanVote", JsonValue.CreateBooleanValue(loc.CanVote));
                cObj.Add("CanReply", JsonValue.CreateBooleanValue(loc.CanReply));
                cObj.Add("CanReport", JsonValue.CreateBooleanValue(loc.CanReport));
                jArr.Add(cObj);
            }

            jObj.Add("data", jArr);
            return jObj.Stringify();
        }

        public static BaseLocation[] Deserialize(String serialized)
        {
            JsonObject jObj = null;
            if (!String.IsNullOrWhiteSpace(serialized) && JsonObject.TryParse(serialized, out jObj) && jObj.ContainsKey("data"))
            {
                List<BaseLocation> listLocs = new List<BaseLocation>();

                JsonArray jArr = jObj.GetNamedArray("data");
                for (uint i = 0; i < jArr.Count; i++)
                {
                    try
                    {
                        JsonObject cObj = jArr.GetObjectAt(i);

                        BaseLocation bl = new BaseLocation();
                        bl.Name = cObj.GetNamedString("Name");
                        bl.Latitude = cObj.GetNamedNumber("Latitude");
                        bl.Longitude = cObj.GetNamedNumber("Longitude");
                        bl.CanSubmit = cObj.GetNamedBoolean("CanSubmit");
                        bl.CanVote = cObj.GetNamedBoolean("CanVote");
                        bl.CanReply = cObj.GetNamedBoolean("CanReply");
                        bl.CanReport = cObj.GetNamedBoolean("CanReport");

                        listLocs.Add(bl);
                    }
                    catch
                    {
                        // Unable to parse that entry.  Oh well
                    }
                }

                return listLocs.ToArray();
            }

            return null;
        }
    }

    public class PeekLocation : BaseLocation
    {
        public int ID { get; private set; }
        public bool IsInactive { get; private set; }
        public bool IsFictional { get; private set; } // Not provided in non-featured locations.  Default=FALSE
        public bool IsLocal { get; private set; }
        public bool IsFeatured { get; private set; }

        /// <summary>
        /// Creates a PeekLocation from a provided JsonObject.  For missing data, fields will assume default values.
        /// If critical fields are missing data, the function will fail.
        /// </summary>
        /// <param name="input">The JsonObject containing the information to build this class.</param>
        /// <returns>A PeekLocation based on input, or null if the input is bad.</returns>
        public static PeekLocation Create(JsonObject input, bool isFeatured)
        {
            if (input.ContainsKey("peekID") && input.ContainsKey("location"))
            {
                PeekLocation p = new PeekLocation();

                p.ID = (int)Helper.GetJSONValue(input, "peekID", ExpectedReturnType.INT, int.MinValue);
                p.Name = (String)Helper.GetJSONValue(input, "location", ExpectedReturnType.STRING, "");
                if (p.ID == int.MinValue || String.IsNullOrWhiteSpace(p.Name))
                {
                    return null;
                }

                p.IsInactive = (bool)Helper.GetJSONValue(input, "invactive", ExpectedReturnType.BOOLEAN, false);
                p.IsFictional = (bool)Helper.GetJSONValue(input, "isFictional", ExpectedReturnType.BOOLEAN, false);
                p.CanSubmit = (bool)Helper.GetJSONValue(input, "canSubmit", ExpectedReturnType.BOOLEAN, false);
                p.CanVote = (bool)Helper.GetJSONValue(input, "canVote", ExpectedReturnType.BOOLEAN, false);
                p.CanReply = (bool)Helper.GetJSONValue(input, "canReply", ExpectedReturnType.BOOLEAN, false);
                p.CanReport = (bool)Helper.GetJSONValue(input, "canReport", ExpectedReturnType.BOOLEAN, false);
                p.IsLocal = (bool)Helper.GetJSONValue(input, "isLocal", ExpectedReturnType.BOOLEAN, false);

                p.Latitude = (double)Helper.GetJSONValue(input, "latitude", ExpectedReturnType.DOUBLE, double.MinValue);
                p.Longitude = (double)Helper.GetJSONValue(input, "longitude", ExpectedReturnType.DOUBLE, double.MinValue);

                p.IsFeatured = isFeatured;

                return p;
            }
            else
            {
                return null;
            }
        }
    }

    public class Notification : INotifyPropertyChanged
    {
        public String ID { get; private set; }
        public String YakID { get; private set; }
        public String Subject { get; private set; }
        public String Body { get; private set; }
        public DateTime Timestamp { get; set; }

        private bool _isRead = false;
        public bool IsRead
        {
            get { return _isRead; }
            set { _isRead = value; OnPropertyChanged("IsRead"); }
        }

        public static Notification Create(JsonObject input)
        {
            if (input.ContainsKey("_id") && input.ContainsKey("thingID"))
            {
                Notification n = new Notification();

                n.ID = (String)Helper.GetJSONValue(input, "_id", ExpectedReturnType.STRING, null);
                n.YakID = (String)Helper.GetJSONValue(input, "thingID", ExpectedReturnType.STRING, null);

                if (n.ID == null || n.YakID == null) return null;

                n.Subject = (String)Helper.GetJSONValue(input, "subject", ExpectedReturnType.STRING, "Notification!");
                n.Body = (String)Helper.GetJSONValue(input, "body", ExpectedReturnType.STRING, "Something Happened...");
                n.IsRead = ((String)Helper.GetJSONValue(input, "status", ExpectedReturnType.STRING, "new")).Equals("read");
                n.Timestamp = (DateTime)Helper.GetJSONValue(input, "updated", ExpectedReturnType.DATETIME, DateTime.Now);

                return n;
            }
            else
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propName)
        {
            if (this.PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }
    }

    public class Location
    {
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public double Accuracy { get; private set; }
        public DateTime Timestamp { get; private set; }

        public Location(Geoposition g)
        {
            Latitude = g.Coordinate.Point.Position.Latitude;
            Longitude = g.Coordinate.Point.Position.Longitude;
            Accuracy = g.Coordinate.Accuracy;
            Timestamp = g.Coordinate.Timestamp.DateTime;
        }

        public Location(double lat, double lon, double acc, DateTime ts)
        {
            Latitude = lat;
            Longitude = lon;
            Accuracy = acc;
            Timestamp = ts;
        }
    }

    public class ThreatCheck
    {
        public String Message;
        public bool AllowContinue;
        public String[] Expressions;

        public ThreatCheck() { }

        /// <summary>
        /// Constructs a ThreatCheck based on a JsonObject
        /// </summary>
        /// <param name="jObj"></param>
        public ThreatCheck(JsonObject jObj)
        {
            if (jObj != null && jObj.ContainsKey("message") && jObj.ContainsKey("allowContinue") && jObj.ContainsKey("expressions"))
            {
                try
                {
                    this.Message = jObj.GetNamedString("message");
                    this.AllowContinue = jObj.GetNamedBoolean("allowContinue");

                    JsonArray expressions = jObj.GetNamedArray("expressions");
                    this.Expressions = new String[expressions.Count];
                    for (uint i = 0; i < expressions.Count; i++)
                        this.Expressions[i] = expressions.GetStringAt(i);
                }
                catch
                {
                    this.Message = null;
                    this.AllowContinue = false;
                    this.Expressions = null;
                }
            }
        }

        /// <summary>
        /// Constructs a ThreatCheck based on a serialized string
        /// </summary>
        /// <param name="serialized"></param>
        public ThreatCheck(String serialized)
        {
            try
            {
                JsonObject jObj = null;
                if (JsonObject.TryParse(serialized, out jObj))
                {
                    this.Message = jObj.GetNamedString("Message");
                    this.AllowContinue = jObj.GetNamedBoolean("AllowContinue");

                    JsonArray jArr = jObj.GetNamedArray("Expressions");
                    this.Expressions = new String[jArr.Count];
                    for (uint i = 0; i < jArr.Count; i++)
                    {
                        this.Expressions[i] = jArr.GetStringAt(i);
                    }
                }
            }
            catch
            {
                this.Message = null;
                this.AllowContinue = false;
                this.Expressions = null;
            }
        }

        /// <summary>
        /// Serializes the ThreatCheck for transmission/storage
        /// </summary>
        /// <returns></returns>
        public String Serialize()
        {
            JsonObject jObj = new JsonObject();
            jObj.Add("Message", JsonValue.CreateStringValue(this.Message));
            jObj.Add("AllowContinue", JsonValue.CreateBooleanValue(this.AllowContinue));

            JsonArray jArr = new JsonArray();
            foreach (String exp in this.Expressions)
                jArr.Add(JsonValue.CreateStringValue(exp));

            jObj.Add("Expressions", jArr);
            return jObj.Stringify();
        }

        /// <summary>
        /// Gets all available ThreatChecks from Settings and deserializes them
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public static List<ThreatCheck> DeserializeFromSettings()
        {
            String[] keys = Settings.ThreatChecksList;

            List<ThreatCheck> threatChecks = new List<ThreatCheck>();
            foreach (String key in keys)
            {
                String raw = (String)Settings.TryGetLocalSetting(key);
                if (raw != null)
                {
                    ThreatCheck tc = new ThreatCheck(raw);
                    if (tc.Message != null && tc.Expressions != null)
                        threatChecks.Add(tc);
                }
            }

            return threatChecks;
        }

        public static void SerializeToSettings(List<ThreatCheck> threatChecks)
        {
            String[] saved = new String[threatChecks.Count];
            for (int i = 0; i < threatChecks.Count; i++)
            {
                Settings.PutLocalSetting(Settings.Keys.ThreatChecksBase + i, threatChecks[i].Serialize());
                saved[i] = Settings.Keys.ThreatChecksBase + i;
            }

            Settings.ThreatChecksList = saved;
        }
    }

    #endregion
}
