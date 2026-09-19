using System;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// Where the fleet is and which key this headset holds. Prototype-grade
    /// storage (PlayerPrefs / env var); a shipped app keeps the key in the
    /// Android Keystore.
    /// </summary>
    public static class FleetConfig
    {
        public const string DefaultBaseUrl = "https://api.opustower.dev/v1";
        private const string KeyPref = "opus.api.key";

        public static string BaseUrl => PlayerPrefs.GetString("opus.api.url", DefaultBaseUrl);

        public static string ApiKey
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("OPUS_API_KEY");
                if (!string.IsNullOrEmpty(env)) return env;
                return PlayerPrefs.GetString(KeyPref, "");
            }
            set
            {
                PlayerPrefs.SetString(KeyPref, value);
                PlayerPrefs.Save();
            }
        }

        public static bool HasKey => ApiKey.StartsWith("osk_");
    }
}
