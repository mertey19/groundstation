using MapboxAccountsUnity;

namespace Mapbox.Unity
{
    using Mapbox.Directions;
    using Mapbox.Geocoding;
    using Mapbox.Map;
    using Mapbox.MapMatching;
    using Mapbox.Platform;
    using Mapbox.Platform.TilesetTileJSON;
    using Mapbox.Tokens;
    using Mapbox.Unity.Telemetry;
    using System;
    using System.IO;
    using UnityEngine;

    public class MapboxAccess : IFileSource
    {
        ITelemetryLibrary _telemetryLibrary;
        IFileSource _fileSource;

        public delegate void TokenValidationEvent(MapboxTokenStatus response);
#pragma warning disable 0067
        public event TokenValidationEvent OnTokenValidation;
#pragma warning restore 0067

        private static MapboxAccess _instance;

        public static MapboxAccess Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MapboxAccess();
                }
                return _instance;
            }
        }

        public static bool Configured;
        public static string ConfigurationJSON;
        private MapboxConfiguration _configuration;
        private string _tokenNotSetErrorMessage = "No configuration file found! Configure your access token from the Mapbox > Setup menu.";

        public MapboxConfiguration Configuration => _configuration;

        MapboxAccess()
        {
            LoadAccessToken();
            if (_configuration == null || string.IsNullOrEmpty(_configuration.AccessToken))
            {
                Debug.LogError(_tokenNotSetErrorMessage);
            }
        }

        public void SetConfiguration(MapboxConfiguration configuration, bool throwExecptions = true)
        {
            if (configuration == null)
            {
                if (throwExecptions)
                {
                    throw new InvalidTokenException(_tokenNotSetErrorMessage);
                }
                return;
            }

            if (string.IsNullOrEmpty(configuration.AccessToken))
            {
                Debug.LogError(_tokenNotSetErrorMessage);
                return;
            }

            _configuration = configuration;

            ConfigureFileSource();
            ConfigureTelemetry();

            Configured = true;
        }

        public void ClearAllCacheFiles()
        {
            string cacheDirectory = Path.Combine(UnityEngine.Application.persistentDataPath, "cache");
            if (!Directory.Exists(cacheDirectory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(cacheDirectory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception deleteEx)
                {
                    Debug.LogErrorFormat("Could not delete [{0}]: {1}", file, deleteEx);
                }
            }

            Debug.Log("done clearing caches");
        }

        private void LoadAccessToken()
        {
            if (string.IsNullOrEmpty(ConfigurationJSON))
            {
                TextAsset configurationTextAsset = Resources.Load<TextAsset>(Constants.Path.MAPBOX_RESOURCES_RELATIVE);
                if (configurationTextAsset == null)
                {
                    throw new InvalidTokenException(_tokenNotSetErrorMessage);
                }

                ConfigurationJSON = configurationTextAsset.text;
            }

#if !WINDOWS_UWP
            var test = JsonUtility.FromJson<MapboxConfiguration>(ConfigurationJSON);
            SetConfiguration(ConfigurationJSON == null ? null : test);
#else
            SetConfiguration(ConfigurationJSON == null ? null : Mapbox.Json.JsonConvert.DeserializeObject<MapboxConfiguration>(ConfigurationJSON));
#endif
        }

        void ConfigureFileSource()
        {
            _fileSource = new FileSource(_configuration.GetMapsSkuToken, _configuration.AccessToken);
        }

        void ConfigureTelemetry()
        {
            try
            {
                _telemetryLibrary = TelemetryFactory.GetTelemetryInstance();
                _telemetryLibrary.Initialize(_configuration.AccessToken);
                _telemetryLibrary.SetLocationCollectionState(GetTelemetryCollectionState());
                _telemetryLibrary.SendTurnstile();
            }
            catch (Exception ex)
            {
                Debug.LogErrorFormat("Error initializing telemetry: {0}", ex);
            }
        }

        public void SetLocationCollectionState(bool enable)
        {
            PlayerPrefs.SetInt(Constants.Path.SHOULD_COLLECT_LOCATION_KEY, enable ? 1 : 0);
            PlayerPrefs.Save();

            if (_telemetryLibrary != null)
            {
                _telemetryLibrary.SetLocationCollectionState(enable);
            }
        }

        bool GetTelemetryCollectionState()
        {
            if (!PlayerPrefs.HasKey(Constants.Path.SHOULD_COLLECT_LOCATION_KEY))
            {
                PlayerPrefs.SetInt(Constants.Path.SHOULD_COLLECT_LOCATION_KEY, 1);
            }

            return PlayerPrefs.GetInt(Constants.Path.SHOULD_COLLECT_LOCATION_KEY) != 0;
        }

        public IAsyncRequest Request(
            string url,
            Action<Response> callback,
            int timeout = 10,
            CanonicalTileId tileId = new CanonicalTileId(),
            string tilesetId = null)
        {
            return _fileSource.Request(url, callback, _configuration.DefaultTimeout, tileId, tilesetId);
        }

        Geocoder _geocoder;
        public Geocoder Geocoder
        {
            get
            {
                if (_geocoder == null)
                {
                    _geocoder = new Geocoder(new FileSource(Instance.Configuration.GetMapsSkuToken, _configuration.AccessToken));
                }
                return _geocoder;
            }
        }

        Directions _directions;
        public Directions Directions
        {
            get
            {
                if (_directions == null)
                {
                    _directions = new Directions(new FileSource(Instance.Configuration.GetMapsSkuToken, _configuration.AccessToken));
                }
                return _directions;
            }
        }

        MapMatcher _mapMatcher;
        public MapMatcher MapMatcher
        {
            get
            {
                if (_mapMatcher == null)
                {
                    _mapMatcher = new MapMatcher(
                        new FileSource(Instance.Configuration.GetMapsSkuToken, _configuration.AccessToken),
                        _configuration.DefaultTimeout
                    );
                }
                return _mapMatcher;
            }
        }

        MapboxTokenApi _tokenValidator;
        public MapboxTokenApi TokenValidator
        {
            get
            {
                if (_tokenValidator == null)
                {
                    _tokenValidator = new MapboxTokenApi();
                }
                return _tokenValidator;
            }
        }

        TileJSON _tileJson;
        public TileJSON TileJSON
        {
            get
            {
                if (_tileJson == null)
                {
                    _tileJson = new TileJSON(
                        new FileSource(Instance.Configuration.GetMapsSkuToken, _configuration.AccessToken),
                        _configuration.DefaultTimeout
                    );
                }
                return _tileJson;
            }
        }

        class InvalidTokenException : Exception
        {
            public InvalidTokenException(string message) : base(message) { }
        }
    }

    [Serializable]
    public class MapboxConfiguration
    {
        [NonSerialized]
        private MapboxAccounts mapboxAccounts = new MapboxAccounts();

        public string AccessToken;
        public uint MemoryCacheSize = 500;
        public uint FileCacheSize = 2500;
        public int DefaultTimeout = 30;
        public bool AutoRefreshCache = false;

        public string GetMapsSkuToken()
        {
            if (mapboxAccounts == null)
            {
                mapboxAccounts = new MapboxAccounts();
            }

            return mapboxAccounts.ObtainMapsSkuUserToken(UnityEngine.Application.persistentDataPath);
        }
    }
}