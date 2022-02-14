using DSharpPlus.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace GiantBombPremiumBot
{
    public class UserManager
    {
        public Dictionary<ulong, User> users = new Dictionary<ulong, User>();

        public void ReadUserInfo()
        {
            users = JUsers.ReadDocument();
        }

        public void WriteUserInfo()
        {
            JUsers.UpdateDocument(users);
        }

        public async Task<bool> IsUserPremium(ulong userID)
        {
            //If they're not in the system, they cannot be premium
            if (!users.ContainsKey(userID))
                return false;
            User user = users[userID];

            if (user.nextCheck > DateTime.UtcNow)
            {
                //If the information if current, use that data
                return user.premiumStatus;
            }

            //If information is outdated, update and return information
            bool status = await UpdateUser(userID);
            WriteUserInfo();
            return status;
        }

        internal async Task<string> GetStatus(ulong userID)
        {
            if (!users.ContainsKey(userID))
            {
                return "We have no record of your Giant Bomb account";
            }
            if (users[userID].nextCheck < DateTime.UtcNow)
                await UpdateUser(userID);
            if (users[userID].premiumStatus)
                return "You're currently listed as a premium user, and will be next checked on <t:" + ((DateTimeOffset)users[userID].nextCheck).ToUnixTimeSeconds() + ":D>.";
            else
                return "You do not currently have premium according to our records. We'll next check on <t:" + ((DateTimeOffset)users[userID].nextCheck).ToUnixTimeSeconds() + ":D>, or do \"Premium Recheck\" to force it to check again.";
        }

        internal string GetRegCode(ulong userID)
        {
            if (users.ContainsKey(userID))
                return users[userID].verificationCode;
            return "";
        }

        public async Task<bool> UpdateUser(ulong userID)
        {
            string URLString = "https://www.giantbomb.com/app/premiumdiscordbot/get-result?regCode=" + users[userID].verificationCode + "&deviceID=dcb";
            XmlTextReader reader = new XmlTextReader(URLString);
            string result = "";
            ulong expiration = 0;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (reader.Name == "status")
                        {
                            result = reader.ReadString();
                        }
                        else if (reader.Name == "expiration")
                        {
                            expiration = ulong.Parse(reader.ReadString());
                        }
                        break;
                    case XmlNodeType.Attribute:
                        break;
                    case XmlNodeType.Text:
                        break;
                    case XmlNodeType.EndElement:
                        break;
                    default:
                        break;
                }
            }
            if (result != "success")
            {
                users[userID].premiumStatus = false;
                users[userID].nextCheck = DateTime.Now.AddMonths(1);
            }
            else if (expiration == 0)
            {
                users[userID].premiumStatus = false;
                users[userID].nextCheck = DateTime.Now.AddMonths(1);
            }
            else
            {
                if (users[userID].premiumStatus == false)
                    users[userID].premiumAdded = DateTime.UtcNow;
                users[userID].premiumStatus = true;
                users[userID].nextCheck = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(expiration).AddDays(1).ToLocalTime();
                if (users[userID].nextCheck < DateTime.Now)
                {
                    users[userID].nextCheck = DateTime.Now.AddDays(7);
                }
            }
            users[userID].lastCheck = DateTime.UtcNow;

            List<DiscordGuild> guilds = new();
            foreach (var shard in Program.Shards)
            {
                foreach (var shardGuild in shard.Discord.Guilds)
                {
                    if (guilds.Contains(shardGuild.Value))
                        continue;
                    else
                        guilds.Add(shardGuild.Value);
                }
            }

            foreach (DiscordGuild guild in guilds)
            {
                var roles = guild.Roles;
                DiscordRole? premiumRole = null;
                DiscordRole? premiumRoleColour = null;

                foreach (var role in roles)
                {
                    if (role.Value.Name == "Premium")
                        premiumRole = role.Value;
                    else if (role.Value.Name == "Primo")
                        premiumRoleColour = role.Value;
                }
                if (premiumRole == null)
                {
                    premiumRole = await guild.CreateRoleAsync("Premium");
                }
                if (premiumRoleColour == null)
                {
                    premiumRoleColour = await guild.CreateRoleAsync("Primo");
                }
                foreach (var user in Program.userManager.users)
                {
                    if (Program.userManager.IsUserRegistered(user.Key) && guild.Members.ContainsKey(user.Key))
                    {
                        var guildUser = guild.Members[user.Key];
                        bool premiumStatus = await Program.userManager.IsUserPremium(user.Key);
                    }
                }
            }


            return users[userID].premiumStatus;
        }

        internal void AddUser(DiscordMember member, string regCode)
        {
            if (users.ContainsKey(member.Id))
            {
                users[member.Id].verificationCode = regCode;
                return;
            }
            User newUser = new User
            {
                username = member.Username,
                id = member.Id,
                discriminator = member.Discriminator,
                verificationCode = regCode,
                lastCheck = DateTime.Now,
                nextCheck = DateTime.MaxValue
            };
            users.Add(member.Id, newUser);
        }

        internal void RemoveUser(ulong userID)
        {
            if (users.ContainsKey(userID))
            {
                users.Remove(userID);
                WriteUserInfo();
            }
        }

        public async Task UpdateAllUsers()
        {
            foreach (var user in users)
            {
                if (user.Value.nextCheck < DateTime.UtcNow)
                    await UpdateUser(user.Key);
            }
            WriteUserInfo();
        }

        public async Task ForceUpdateAllUsers()
        {
            foreach (var user in users)
            {
                await UpdateUser(user.Key);
            }
            WriteUserInfo();
        }

        internal DateTime GetNextCheckTime()
        {
            DateTime nextCheck = DateTime.MaxValue;
            foreach (var user in users)
            {
                if (user.Value.nextCheck < nextCheck)
                    nextCheck = user.Value.nextCheck;
            }
            return nextCheck;
        }

        internal bool IsUserRegistered(ulong userID)
        {
            return users.ContainsKey(userID);
        }
    }

    public class Role
    {
        public string name;
        public ulong guild;
        public ulong id;
    }

    public class User
    {
        public ulong id;
        public string username;
        public string discriminator;
        public bool premiumStatus = false;
        public DateTime lastCheck;
        public DateTime nextCheck;
        public string verificationCode;
        public DateTime premiumAdded;
    }

    public class Users
    {
        public List<User> users = new List<User>();
    }

    public static class JUsers
    {
        public static Dictionary<ulong, User> ReadDocument()
        {
            var cfg = new CryptoConfig();
            var json = string.Empty;
            if (!File.Exists("config.json"))
            {
                json = JsonConvert.SerializeObject(cfg);
                File.WriteAllText("crypto.json", json, new UTF8Encoding(false));
                Console.WriteLine("crypto config file was not found, a new one was generated. Fill it with proper values and rerun this program");
                Console.ReadKey();

                return null;
            }

            byte[] key = System.Text.Encoding.ASCII.GetBytes(cfg.Key);
            byte[] iv = System.Text.Encoding.ASCII.GetBytes(cfg.IV);
            json = File.ReadAllText("crypto.json", new UTF8Encoding(false));
            cfg = JsonConvert.DeserializeObject<CryptoConfig>(json);
            Dictionary<ulong, User> result = new Dictionary<ulong, User>();
            List<User> users = new List<User>();
            string path = "users.txt";
            if (File.Exists(path))
            {
                Byte[] data = File.ReadAllBytes(path);
                string decrypted = Crypto.DecryptStringFromBytes_Aes(data, key, iv);

                users = JsonConvert.DeserializeObject<Users>(decrypted).users;
            }
            foreach (var user in users)
            {
                if (!result.ContainsKey(user.id))
                {
                    result.Add(user.id, user);

                }
                if (user.premiumAdded < new DateTime(2000, 1, 1))
                {
                    result[user.id].premiumAdded = user.lastCheck;
                }
                if (user.nextCheck < new DateTime(2000, 1, 1))
                {
                    result[user.id].nextCheck = DateTime.Now;
                }
                if (user.nextCheck < Program.nextRun)
                {
                    Program.nextRun = user.nextCheck;
                }
            }
            return result;
        }
        public static void UpdateDocument(Dictionary<ulong, User> users)
        {
            var cfg = new CryptoConfig();
            var json = string.Empty;
            if (!File.Exists("config.json"))
            {
                json = JsonConvert.SerializeObject(cfg);
                File.WriteAllText("crypto.json", json, new UTF8Encoding(false));
                Console.WriteLine("crypto config file was not found, a new one was generated. Fill it with proper values and rerun this program");
                Console.ReadKey();

                return;
            }

            byte[] key = System.Text.Encoding.ASCII.GetBytes(cfg.Key);
            byte[] iv = System.Text.Encoding.ASCII.GetBytes(cfg.IV);

            List<User> data = new List<User>();
            foreach (var user in users)
            {
                data.Add(user.Value);
            }
            string path = "users.txt";
            Users userObject = new Users();
            userObject.users = data;
            string dataString = JsonConvert.SerializeObject(userObject);
            byte[] encrypted = Crypto.EncryptStringToBytes_Aes(dataString, key, iv);
            File.WriteAllBytes(path, encrypted);

        }
    }
    static public class Crypto
    {
        static public byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");
            byte[] encrypted;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }

        static public string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = "";

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.BlockSize = 128;
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {

                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
            return plaintext;
        }
    }
}
