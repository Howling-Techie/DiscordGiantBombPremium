
using DSharpPlus.Entities;
using MySqlConnector;
using System.Data;
using System.Security.Cryptography;
using System.Xml;

//THIS IS ALL TEMP CODE IN CASE YOU WANT TO USE A MORE SECURE DATABASE LIKE MYSQL INSTEAD OF SQLITE.


namespace GiantBombPremiumBot
{
    internal class User_Manager___MySQL
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
                //If the user is not premium, check in a day
                user.premiumStatus = false;
                user.nextCheck = DateTime.Now.AddDays(1);
            }
            else if (expiration == 0)
            {
                //If the user is premium but no expiration date was provided, check in a day
                user.premiumStatus = true;
                user.nextCheck = DateTime.Now.AddDays(1);
            }
            else
            {
                //Premium and has an expiration date! Excellent
                user.premiumStatus = true;
                user.nextCheck = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(expiration).AddDays(1).ToLocalTime();
                if (user.nextCheck < DateTime.Now)
                {
                    //If an invalid expiration date has been returned, check in a day
                    user.nextCheck = DateTime.Now.AddDays(1);
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
                catch
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
                    try
                    {
                        //Sometimes it gets this far if the user is not on the server, despite the previous try catch...
                        await member.GrantRoleAsync(premiumRole);
                    }
                    catch
                    {
                        RemoveUser(user.id);
                    }
                }
                else
                {
                    if (member.Roles.Contains(premiumRole))
                    {
                        try
                        {
                            await member.RevokeRoleAsync(premiumRole);
                        }
                        catch
                        {
                            RemoveUser(user.id);
                            return false;
                        }
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

        //Gift discord premium to a user, requires them to have a linked account
        internal static bool GiftUser(ulong userID, ulong additional)
        {
            User? user = Database.GetUser(userID);
            if (user != null)
            {
                Database.UpdateUserStatus(userID, user.nextCheck.AddSeconds(additional), true);
                return true;
            }
            else return false;
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
            List<ulong> users = Database.GetAllUserIDs();
            foreach (ulong userID in users)
            {
                User? user = Database.GetUser(userID);
                if (user != null)
                    await UpdateUser(userID);
            }
        }

        internal static List<User> GetAllUsers()
        {
            return Database.GetAllUsers();
        }

        internal static User? GetUser(ulong userID)
        {
            return Database.GetUser(userID);
        }

        //Reset's a user's premium to what it should be
        internal static bool RevokeGiftUser(ulong userID)
        {
            User? user = Database.GetUser(userID);
            if (user != null)
            {
                UpdateUser(userID);
                return true;
            }
            else return false;
        }

        private static class Database
        {
            //Initiates the crypto data from the config json file
            private static readonly CryptoConfig cfg = new();

            //Gets a user from the database
            public static User? GetUser(ulong userId)
            {
                //Check if user exists
                bool exists = false;
                User? user = null;
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();

                var command = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM Users WHERE UserID = @id)");
                command.Parameters.AddWithValue("@id", userId);
                int result = Convert.ToInt32(command.ExecuteScalar());
                if (result == 1)
                    exists = true;
                if (exists)
                {
                    command = new MySqlCommand("SELECT UserID, PremiumCode, Expiration, Status FROM Users WHERE UserID = @id LIMIT 1");
                    command.Parameters.AddWithValue("@id", userId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string verificationCode = reader.GetString(1);
                            user = new User(userId, verificationCode);
                            user.nextCheck = reader.GetDateTime(2);
                            user.premiumStatus = bool.Parse(reader.GetString(3));

                        }
                    }
                }
                connection.Close();
                return user;
            }

            //Updates a user's info in the database (or creates an entry if they don't)
            public static void UpdateUser(User user)
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();

                var command = new MySqlCommand("REPLACE INTO Users VALUES (@id, @code, @expiration, @status)");
                    command.Parameters.AddWithValue("@id", user.id);
                    command.Parameters.AddWithValue("@code", user.verificationCode);
                    command.Parameters.AddWithValue("@expiration", user.nextCheck);
                    command.Parameters.AddWithValue("@status", user.premiumStatus.ToString());
                    command.ExecuteNonQuery();
                connection.Close();
            }

            //Updates a user's entry (assuming the exist in the database). Mainly used for updating a user after getting a new expiration date
            public static void UpdateUserStatus(ulong userID, DateTime nextCheck, bool status)
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();

                var command = new MySqlCommand("UPDATE Users SET Expiration = @expiration, Status = @status WHERE UserID = @id");
                    command.Parameters.AddWithValue("@id", userID);
                    command.Parameters.AddWithValue("@expiration", nextCheck);
                    command.Parameters.AddWithValue("@status", status.ToString());
                    command.ExecuteNonQuery();

                connection.Close();
            }

            //Remove a user from the database
            public static void RemoveUser(ulong userID)
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();

                var command = new MySqlCommand("DELETE FROM Users WHERE UserID = @id");
                command.Parameters.AddWithValue("@id", userID);
                command.ExecuteNonQuery();
                connection.Close();
            }

            //return the earliest time a user needs to be checked
            internal static DateTime GetNextCheckTime()
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();
                var command = new MySqlCommand("SELECT Expiration FROM Users ORDER BY Expiration ASC LIMIT 1");
                DateTime nextCheck = DateTime.Now;

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        nextCheck = reader.GetDateTime(0);
                    }
                }
                connection.Close();
                return nextCheck;
            }

            //Get all users, return a list of those overdue for a check in
            internal static List<ulong> GetExpiredUsers()
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();
                var command = new MySqlCommand("SELECT UserID FROM Users WHERE Expiration < now()");
                List<ulong> expiredUsers = new();

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        expiredUsers.Add(reader.GetUInt64(0));
                    }
                }
                connection.Close();
                return expiredUsers;
            }

            //Get all userIDs, return all regardless of next check in
            internal static List<ulong> GetAllUserIDs()
            {
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();
                var command = new MySqlCommand("SELECT UserID FROM Users");
                List<ulong> allUsers = new();

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        allUsers.Add(reader.GetUInt64(0));
                    }
                }
                connection.Close();
                return allUsers;
            }

            //Get all users, return all regardless of next check in
            internal static List<User> GetAllUsers()
            {
                List<User> users = new();
                using var connection = new MySqlConnection("Server=localhost;User ID=DiscordBot;Password={PASSWORD};Database=GBPremium");
                connection.Open();

                var command = new MySqlCommand("SELECT UserID, PremiumCode, Expiration, Status FROM Users");
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ulong userID = reader.GetUInt64(0);
                        string verificationCode = reader.GetString(1);
                        DateTime nextCheck = reader.GetDateTime(2);
                        bool premium = bool.Parse(reader.GetString(3));
                        User tempUser = new User(userID, verificationCode);
                        tempUser.nextCheck = nextCheck;
                        tempUser.premiumStatus = premium;
                        users.Add(tempUser);
                    }
                }
                connection.Close();
                return users;
            }
        }
    }
}
