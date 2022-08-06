using DSharpPlus.Entities;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Xml;

namespace GiantBombPremiumBot
{
    public class UserManager
    {
        //Return if a given user is premium
        public static async Task<bool> GetPremiumStatus(ulong userID)
        {
            User? user = Database.GetUser(userID);
            //If they're not in the system, they cannot be premium
            if (user == null)
                return false;

            if (user.nextCheck > DateTime.UtcNow)
            {
                //If the information if current, use that data
                return user.premiumStatus;
            }

            //If information is outdated, update and return information
            bool status = await UpdateUser(userID);
            return status;
        }

        //Return a string of their status and date of next check
        internal async Task<string> GetStatus(ulong userID)
        {
            User? user = Database.GetUser(userID);
            if (user == null)
            {
                return "We have no record of your Giant Bomb account";
            }
            if (user.nextCheck < DateTime.UtcNow)
            {
                await UpdateUser(userID);
                return await GetStatus(userID);
            }
            if (user.premiumStatus)
                return "You're currently listed as a premium user, and will be next checked on <t:" + ((DateTimeOffset)user.nextCheck).ToUnixTimeSeconds() + ":D>.";
            else
                return "You do not currently have premium according to our records. We'll next check on <t:" + ((DateTimeOffset)user.nextCheck).ToUnixTimeSeconds() + ":D>, or do \"Premium Recheck\" to force it to check again.";
        }

        internal static string GetUserVerifCode(ulong userID)
        {
            User? user = Database.GetUser(userID);
            if (user == null)
                return "";
            else
                return user.verificationCode;
        }

        //Update a given user's premium status, regardless of expiration date
        public static async Task<bool> UpdateUser(ulong userID)
        {
            User? user = Database.GetUser(userID);
            string result = "";
            ulong expiration = 0;
            if (user == null)
                return false;
            if (user.verificationCode is null or "")
            {
                result = "failure";
            }
            else
            {
                string URLString = "https://www.giantbomb.com/app/premiumdiscordbot/get-result?regCode=" + user.verificationCode + "&deviceID=dcb";
                int attempts = 0;
                bool success = false;
                XmlTextReader? reader = null;
                while (!success && attempts < 10)
                {
                    try
                    {
                        reader = new XmlTextReader(URLString);
                        success = true;
                    }
                    catch
                    {
                        attempts++;
                    }
                }
                if (!success || reader == null)
                    return true;
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
            }
            if (result != "success")
            {
                //If the user is not premium, check in a week
                user.premiumStatus = false;
                user.nextCheck = DateTime.Now.AddDays(7);
            }
            else if (expiration == 0)
            {
                //If the user is premium but no expiration date was provided, check in a week
                user.premiumStatus = false;
                user.nextCheck = DateTime.Now.AddDays(7);
            }
            else
            {
                user.premiumStatus = true;
                user.nextCheck = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(expiration).AddDays(1).ToLocalTime();
                if (user.nextCheck < DateTime.Now)
                {
                    //If an invalid expiration date has been returned, check in a week
                    user.nextCheck = DateTime.Now.AddDays(7);
                }
            }

            //Get a list of servers the bot is connected to
            List<DiscordGuild> guilds = new();
            foreach (PremiumBot? shard in Program.Shards)
            {
                foreach (KeyValuePair<ulong, DiscordGuild> shardGuild in shard.Discord.Guilds)
                {
                    if (guilds.Contains(shardGuild.Value))
                        continue;
                    else
                        guilds.Add(shardGuild.Value);
                }
            }

            //Go through the list to get the premium roles in each channel
            foreach (DiscordGuild guild in guilds)
            {
                IReadOnlyDictionary<ulong, DiscordRole>? roles = guild.Roles;
                DiscordRole? premiumRole = null;
                DiscordRole? premiumRoleColour = null;
                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(user.id);
                }
                catch (Exception)
                {
                    RemoveUser(user.id);
                    return false;
                }

                foreach (KeyValuePair<ulong, DiscordRole> role in roles)
                {
                    if (role.Value.Name == "Premium")
                        premiumRole = role.Value;
                    if (role.Value.Name == "Primo")
                        premiumRoleColour = role.Value;
                }
                //If the user is premium, give them the premium role, otherwise revoke the role as well as the colour role if it's enabled
                if (user.premiumStatus)
                {
                    await member.GrantRoleAsync(premiumRole);
                }
                else
                {
                    if (member.Roles.Contains(premiumRole))
                    {
                        await member.RevokeRoleAsync(premiumRole);
                    }
                    if (member.Roles.Contains(premiumRoleColour))
                    {
                        await member.RevokeRoleAsync(premiumRoleColour);
                    }
                }
            }

            //Store/update the user's premium status and return said status.
            Database.UpdateUser(user);
            return user.premiumStatus;
        }

        //Get the next time users need to be checked
        internal static DateTime GetNextCheckTime()
        {
            return Database.GetNextCheckTime();
        }

        //Add a user to the database, with their registration code
        internal static void AddUser(ulong userID, string regCode)
        {
            //Check if the user is already in the database, otherwise we just need to update them
            User? user = Database.GetUser(userID);
            if (user != null)
            {
                user.verificationCode = regCode;
                Database.UpdateUser(user);
                return;
            }
            User newUser = new(userID, regCode)
            {
                nextCheck = DateTime.MaxValue
            };
            Database.UpdateUser(newUser);
        }

        //Remove a user from the database
        internal static void RemoveUser(ulong userID)
        {
            Database.RemoveUser(userID);
        }

        //Update all users that are overdue (this should be done automatically)
        public static async Task UpdateAllUsers()
        {
            List<ulong> users = Database.GetExpiredUsers();
            foreach (ulong userID in users)
            {
                User? user = Database.GetUser(userID);
                if (user != null)
                    if (user.nextCheck < DateTime.UtcNow)
                        await UpdateUser(userID);
            }
        }

        //Update all users, regardless of when they are due
        public static async Task ForceUpdateAllUsers()
        {
            List<ulong> users = Database.GetAllUsers();
            foreach (ulong userID in users)
            {
                User? user = Database.GetUser(userID);
                if (user != null)
                    if (user.nextCheck < DateTime.UtcNow)
                        await UpdateUser(userID);
            }
        }

        private static class Database
        {
            //Initiates the crypto data from the config json file
            private static readonly CryptoConfig cfg = new();

            //Gets a user from the database
            public static User? GetUser(ulong userId)
            {
                byte[] encrID = Crypto.EncryptStringToBytes_Aes(userId.ToString(), cfg.Key, cfg.IV);

                string connectionString = "Data Source=GBPremium.db;";

                //Check if user exists
                bool exists = false;
                User? user = null;
                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT EXISTS(SELECT 1 FROM Users WHERE UserID = @id)";
                    command.CommandText = comText;
                    command.Parameters.AddWithValue("@id", encrID);
                    int result = Convert.ToInt32(command.ExecuteScalar());
                    if (result == 1)
                        exists = true;
                    connection.Close();
                }
                if (exists)
                    using (SqliteConnection connection = new(connectionString))
                    {
                        connection.Open();

                        SqliteCommand? command = connection.CreateCommand();
                        string comText = "SELECT UserID, PremiumCode, Expiration, Status FROM Users WHERE UserID = @id LIMIT 1";
                        command.CommandText = comText;
                        command.Parameters.AddWithValue("@id", encrID);
                        using (SqliteDataReader? reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Stream idStream = reader.GetStream(0);
                                byte[] idData = new byte[idStream.Length];
                                idStream.Seek(0, SeekOrigin.Begin);
                                idStream.Read(idData, 0, idData.Length);

                                Stream premiumStream = reader.GetStream(1);
                                byte[] premiumData = new byte[premiumStream.Length];
                                premiumStream.Seek(0, SeekOrigin.Begin);
                                premiumStream.Read(premiumData, 0, premiumData.Length);
                                user = new User(ulong.Parse(Crypto.DecryptStringFromBytes_Aes(idData, cfg.Key, cfg.IV)),
                                    Crypto.DecryptStringFromBytes_Aes(premiumData, cfg.Key, cfg.IV));
                                ulong expirationTime = ulong.Parse(reader.GetString(2));

                                user.nextCheck = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(expirationTime);

                                user.premiumStatus = bool.Parse(reader.GetString(3));

                            }
                        }
                        connection.Close();
                    }
                return user;
            }

            //Updates a user's info in the database (or creates an entry if they don't)
            public static void UpdateUser(User user)
            {
                byte[] encrID = Crypto.EncryptStringToBytes_Aes(user.id.ToString(), cfg.Key, cfg.IV);
                byte[] encrCode = Crypto.EncryptStringToBytes_Aes(user.verificationCode.ToString(), cfg.Key, cfg.IV);

                string connectionString = "Data Source=GBPremium.db;";

                //Check if user exists
                bool exists = false;
                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT EXISTS(SELECT 1 FROM Users WHERE UserID = @id)";
                    command.CommandText = comText;
                    command.Parameters.AddWithValue("@id", encrID);
                    int result = Convert.ToInt32(command.ExecuteScalar());
                    if (result == 1)
                        exists = true;
                    connection.Close();
                }
                //If they don't exist, insert the data, otherwise just update
                if (!exists)
                    using (SqliteConnection connection = new(connectionString))
                    {
                        connection.Open();

                        SqliteCommand? command = connection.CreateCommand();
                        string comText = "INSERT INTO Users(UserID, PremiumCode, Expiration, Status) VALUES (@id, @code, @expiration, @status)";
                        command.CommandText = comText;
                        command.Parameters.AddWithValue("@id", encrID);
                        command.Parameters.AddWithValue("@code", encrCode);
                        command.Parameters.AddWithValue("@expiration", ((DateTimeOffset)user.nextCheck).ToUnixTimeSeconds().ToString());
                        command.Parameters.AddWithValue("@status", user.premiumStatus.ToString());
                        command.ExecuteNonQuery();
                        connection.Close();
                    }
                else
                {
                    using SqliteConnection connection = new(connectionString);
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "UPDATE Users SET PremiumCode = @code, Expiration = @expiration, Status = @status WHERE UserID = @id";
                    command.CommandText = comText;
                    command.Parameters.AddWithValue("@id", encrID);
                    command.Parameters.AddWithValue("@code", encrCode);
                    command.Parameters.AddWithValue("@expiration", ((DateTimeOffset)user.nextCheck).ToUnixTimeSeconds().ToString());
                    command.Parameters.AddWithValue("@status", user.premiumStatus.ToString());
                    command.ExecuteNonQuery();
                    connection.Close();
                }
            }

            //Updates a user's entry (assuming the exist in the database). Mainly used for updating a user after getting a new expiration date
            public static void UpdateUserStatus(long userID, ulong nextCheck, bool status)
            {
                byte[] encrID = Crypto.EncryptStringToBytes_Aes(userID.ToString(), cfg.Key, cfg.IV);

                string connectionString = "Data Source=GBPremium.db;";

                //Check if user exists
                bool exists = false;
                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT EXISTS(SELECT 1 FROM Users WHERE UserID = @id)";
                    command.CommandText = comText;
                    command.Parameters.AddWithValue("@id", encrID);
                    int result = Convert.ToInt32(command.ExecuteScalar());
                    if (result == 1)
                        exists = true;
                    connection.Close();
                }
                if (exists)
                    using (SqliteConnection connection = new(connectionString))
                    {
                        connection.Open();

                        SqliteCommand? command = connection.CreateCommand();
                        string comText = "UPDATE Users SET Expiration = @expiration, Status = @status WHERE UserID = @id";
                        command.CommandText = comText;
                        command.Parameters.AddWithValue("@id", encrID);
                        command.Parameters.AddWithValue("@expiration", nextCheck.ToString());
                        command.Parameters.AddWithValue("@status", status.ToString());
                        command.ExecuteNonQuery();
                        connection.Close();
                    }
            }

            //Remove a user from the database
            public static void RemoveUser(ulong userID)
            {
                byte[] encrID = Crypto.EncryptStringToBytes_Aes(userID.ToString(), cfg.Key, cfg.IV);
                string connectionString = "Data Source=GBPremium.db;";
                using SqliteConnection connection = new(connectionString);
                connection.Open();

                SqliteCommand? command = connection.CreateCommand();
                string comText = "DELETE FROM Users WHERE UserID = @id";
                command.CommandText = comText;
                command.Parameters.AddWithValue("@id", encrID);
                command.ExecuteNonQuery();
                connection.Close();
            }

            //return the earliest time a user needs to be checked
            internal static DateTime GetNextCheckTime()
            {
                string connectionString = "Data Source=GBPremium.db;";
                ulong nextCheck = ulong.MaxValue;

                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT Expiration FROM Users";
                    command.CommandText = comText;
                    using (SqliteDataReader? reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ulong expirationTime = ulong.Parse(reader.GetString(0));
                            if (expirationTime < nextCheck)
                                nextCheck = expirationTime;

                        }
                    }
                    connection.Close();
                }
                return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(nextCheck);
            }

            //Get all users, return a list of those overdue for a check in
            internal static List<ulong> GetExpiredUsers()
            {
                string connectionString = "Data Source=GBPremium.db;";
                List<ulong> expiredUsers = new();

                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT UserID, Expiration FROM Users";
                    command.CommandText = comText;
                    using (SqliteDataReader? reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ulong expirationTime = ulong.Parse(reader.GetString(1));

                            if (expirationTime < (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                            {
                                Stream idStream = reader.GetStream(0);
                                byte[] idData = new byte[idStream.Length];
                                idStream.Seek(0, SeekOrigin.Begin);
                                idStream.Read(idData, 0, idData.Length);
                                expiredUsers.Add(ulong.Parse(Crypto.DecryptStringFromBytes_Aes(idData, cfg.Key, cfg.IV)));
                            }
                        }
                    }
                    connection.Close();
                }
                return expiredUsers;
            }

            //Get all users, return all regardless of next check in
            internal static List<ulong> GetAllUsers()
            {
                string connectionString = "Data Source=GBPremium.db;";
                List<ulong> allUsers = new();

                using (SqliteConnection connection = new(connectionString))
                {
                    connection.Open();

                    SqliteCommand? command = connection.CreateCommand();
                    string comText = "SELECT UserID FROM Users";
                    command.CommandText = comText;
                    using (SqliteDataReader? reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            allUsers.Add(ulong.Parse(reader.GetString(0)));

                        }
                    }
                    connection.Close();
                }
                return allUsers;
            }
        }
    }

    public class User
    {
        public User(ulong id, string verificationCode)
        {
            this.id = id;
            this.verificationCode = verificationCode;
        }
        public ulong id;
        public bool premiumStatus = false;
        public DateTime nextCheck;
        public string verificationCode;
    }

    public class Users
    {
        public List<User> users = new();
    }


    public static class Crypto
    {
        public static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException(nameof(plainText));
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException(nameof(Key));
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException(nameof(IV));
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
                using MemoryStream msEncrypt = new();
                using CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write);
                using (StreamWriter swEncrypt = new(csEncrypt))
                {
                    //Write all data to the stream.
                    swEncrypt.Write(plainText);
                }
                encrypted = msEncrypt.ToArray();
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }

        public static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException(nameof(cipherText));
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException(nameof(Key));
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException(nameof(IV));

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
                using MemoryStream msDecrypt = new(cipherText);
                using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
                using StreamReader srDecrypt = new(csDecrypt);

                // Read the decrypted bytes from the decrypting stream
                // and place them in a string.
                plaintext = srDecrypt.ReadToEnd();
            }
            return plaintext;
        }
    }
}
