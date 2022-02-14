
using Newtonsoft.Json;

namespace GiantBombPremiumBot
{
    public sealed class BotConfig
    {
        [JsonProperty("token")]
        public string Token { get; private set; } = string.Empty;

        [JsonProperty("command_prefixes")]
        public string[] CommandPrefixes { get; private set; } = new[] { "!" };

        [JsonProperty("shards")]
        public int ShardCount { get; private set; } = 1;
    }
}