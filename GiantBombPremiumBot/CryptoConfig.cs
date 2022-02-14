
using Newtonsoft.Json;

namespace GiantBombPremiumBot
{
    public sealed class CryptoConfig
    {
        [JsonProperty("key")]
        public string Key { get; private set; } = string.Empty;

        [JsonProperty("iv")]
        public string IV { get; private set; } = string.Empty;
    }
}