
using Newtonsoft.Json;
using System.Text;

namespace GiantBombPremiumBot
{
    public sealed class CryptoConfig
    {
        public CryptoConfig()
        {
            string? json = string.Empty;
            if (!File.Exists("crypto.json"))
            {
                json = JsonConvert.SerializeObject(new CryptoConfig());
                File.WriteAllText("crypto.json", json, new UTF8Encoding(false));
                Console.WriteLine("crypto config file was not found, a new one was generated. Fill it with proper values and rerun this program");
                Console.ReadKey();

                return;
            }
            json = File.ReadAllText("crypto.json", new UTF8Encoding(false));
            Key = Encoding.ASCII.GetBytes(JsonConvert.DeserializeObject<CryptoJSON>(json).Key);
            IV = Encoding.ASCII.GetBytes(JsonConvert.DeserializeObject<CryptoJSON>(json).IV);
        }

        public byte[] Key { get; private set; } = new byte[0];
        public byte[] IV { get; private set; } = new byte[0];
    }

    public sealed class CryptoJSON
    {
        [JsonProperty("key")]
        public string Key { get; private set; } = string.Empty;

        [JsonProperty("iv")]
        public string IV { get; private set; } = string.Empty;
    }
}