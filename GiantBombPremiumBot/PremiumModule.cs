using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Data.Sqlite;
using System.Xml;

namespace GiantBombPremiumBot
{
    public class RequireUserRole : ContextMenuCheckBaseAttribute
    {
        public string RoleName;
        public RequireUserRole(string roleName)
        {
            this.RoleName = roleName;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task<bool> ExecuteChecksAsync(ContextMenuContext ctx)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            if (ctx.Guild.GetMemberAsync(ctx.User.Id).Result.Roles.Any(x => x.Name == RoleName))
                return true;
            else
                return false;
        }

    }

    internal class BlankModule : ApplicationCommandModule
    {

    }

    [SlashCommandGroup("Premium", "Commands for Giant Bomb Premium")]
    public class PremiumModule : ApplicationCommandModule
    {
        [SlashCommand("Connect", "Link your Discord account with Giant Bomb")]
        public static async Task ConnectCommand(InteractionContext ctx)
        {
            //This checks the database for the user, and if they're not present, generates a verification code
            await UserManager.UpdateUser(ctx.Member.Id);
            //Checks if the user is already premium
            bool premium = await UserManager.GetPremiumStatus(ctx.User.Id);
            if (premium)
            {
                DiscordInteractionResponseBuilder AlreadyPremiumResponse = new()
                {
                    Content = "You're already premium!"
                };
                AlreadyPremiumResponse.AsEphemeral(true);
                await ctx.CreateResponseAsync(AlreadyPremiumResponse);
                return;
            }
            string regCode = UserManager.GetUserVerifCode(ctx.User.Id);
            //The regCode *should* have been generated in GetPremiumStatus but best to double check
            if (regCode is "" or null)
            {
                string URLString = "https://www.giantbomb.com/app/premiumdiscordbot/get-code?deviceID=dcb";
                XmlTextReader? reader = null;
                int attempts = 0;
                bool success = false;
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
                if (reader != null)
                    while (reader.Read())
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                if (reader.Name == "regCode")
                                {
                                    regCode = reader.ReadString();
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
                if (regCode is "" or null)
                {
                    DiscordInteractionResponseBuilder ErrorResponse = new()
                    {
                        Content = "Oops, something went wrong :("
                    };
                    ErrorResponse.AsEphemeral(true);
                    await ctx.CreateResponseAsync(ErrorResponse);
                    return;
                }
            }
            UserManager.AddUser(ctx.User.Id, regCode);

            //Generate initial response
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                Content = "Hi! To complete the process of linking your Giant Bomb account to your Discord account, just need to do a few quick things!" +
                "\n1. Visit the link below and enter the code **" + regCode + "**." +
                "\n2. Hit the big \"Hook me Up!\" button." +
                "\n3. Select \"Verify\" below, and we'll do the rest." +
                "\n  If there's an issue verifying, hit \"Reset\" and we'll generate a new code."
            };
            List<DiscordComponent> row0 = new()
            {
                new DiscordLinkButtonComponent("https://www.giantbomb.com/app/premiumdiscordbot/activate", "giantbomb.com", false, new DiscordComponentEmoji(588743258130219010))
            };
            List<DiscordComponent> row1 = new()
            {
                new DiscordButtonComponent(ButtonStyle.Primary, "verify", "Verify", false, new DiscordComponentEmoji(859388130411282442))
            };
            List<DiscordComponent> row2 = new()
            {
                new DiscordButtonComponent(ButtonStyle.Danger, "reset", "Reset", false, new DiscordComponentEmoji(868122243845206087))
            };
            responseBuilder.AddComponents(row0);
            responseBuilder.AddComponents(row1);
            responseBuilder.AddComponents(row2);
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;

        }

        [SlashCommand("Recheck", "If you've resubbed to premium, run this command.")]
        public static async Task RecheckCommand(InteractionContext ctx)
        {
            //A simple command a user can run to force the bot to re-check if a user is premium and update their roles accordingly
            await UserManager.UpdateUser(ctx.Member.Id);
            bool premium = await UserManager.UpdateUser(ctx.Member.Id);
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                IsEphemeral = true,
                Content = premium ? "After rechecking, you are premium" : "After rechecking, you are not premium"
            };
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Status", "Get your link status")]
        public static async Task StatusCommand(InteractionContext ctx)
        {
            string status = await Program.UserManager.GetStatus(ctx.User.Id);
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                Content = status
            };
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Info", "About the bot")]
        public static async Task InfoCommand(InteractionContext ctx)
        {
            //This will probably have to change, however unless all icons are changed, they must remain credited
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                Content = "Bot programmed by Howling Techie, icons by @icons_discord on Twitter"
            };
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Unlink", "Remove the Giant Bomb account associated with this Discord account")]
        public static async Task UnlinkCommand(InteractionContext ctx)
        {
            //Removes a user from being premium. Not sure what use this has but it's good to have.
            DiscordRole? premiumRole = null;
            DiscordRole? premiumRoleColour = null;
            //Search the server for the Premium and Primo roles
            foreach (KeyValuePair<ulong, DiscordRole> role in ctx.Member.Guild.Roles)
            {
                if (role.Value.Name == "Premium")
                    premiumRole = role.Value;
                else if (role.Value.Name == "Primo")
                    premiumRoleColour = role.Value;
            }
            //Remove user's premium roles
            if (ctx.Member.Roles.Contains(premiumRole))
            {
                await ctx.Member.RevokeRoleAsync(premiumRole);
            }
            if (ctx.Member.Roles.Contains(premiumRoleColour))
            {
                await ctx.Member.RevokeRoleAsync(premiumRoleColour);
            }

            //Remove the user from the database
            UserManager.RemoveUser(ctx.Member.Id);
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                Content = "You've been removed from the system."
            };
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Remove", "Forcably unlink a user from their Giant Bomb account; useful for debugging")]
        [RequireUserRole("Moderators")]
        public static async Task RemoveCommand(InteractionContext ctx, [Option("User", "User to remove")] DiscordUser user)
        {
            DiscordMember member = await ctx.Guild.GetMemberAsync(user.Id);
            //Mainly for debugging, removes a user from the database.
            DiscordRole? premiumRole = null;
            DiscordRole? premiumRoleColour = null;
            //Search the server for the Premium and Primo roles
            foreach (KeyValuePair<ulong, DiscordRole> role in member.Guild.Roles)
            {
                if (role.Value.Name == "Premium")
                    premiumRole = role.Value;
                else if (role.Value.Name == "Primo")
                    premiumRoleColour = role.Value;
            }
            //Remove user's premium roles
            if (member.Roles.Contains(premiumRole))
            {
                await member.RevokeRoleAsync(premiumRole);
            }
            if (member.Roles.Contains(premiumRoleColour))
            {
                await member.RevokeRoleAsync(premiumRoleColour);
            }
            //Remove from database
            UserManager.RemoveUser(user.Id);
            DiscordInteractionResponseBuilder responseBuilder = new()
            {
                Content = user.Username + " has been removed from the system."
            };
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("CleanUp", "Go through all users on the server, and if they have premium, double check the validity")]
        [RequireUserRole("Moderators")]
        public static async Task CleanUpCommand(InteractionContext ctx)
        {
            //Checks all members on the server. USES A LOT OF TIME AND REQUESTS
            DiscordGuild? server = ctx.Guild;
            IReadOnlyDictionary<ulong, DiscordRole>? roles = server.Roles;
            DiscordRole? premiumRole = null;
            DiscordRole? premiumRoleColour = null;
            int revoked = 0;
            int fine = 0;
            DiscordInteractionResponseBuilder initResponseBuilder = new()
            {
                Content = "Checking users that may need the premium role removed. This can take a few minutes."
            };
            await ctx.CreateResponseAsync(initResponseBuilder);

            IReadOnlyCollection<DiscordMember>? members = await server.GetAllMembersAsync();

            foreach (KeyValuePair<ulong, DiscordRole> role in roles)
            {
                if (role.Value.Name == "Premium")
                    premiumRole = role.Value;
                if (role.Value.Name == "Primo")
                    premiumRoleColour = role.Value;
            }

            foreach (DiscordMember? member in members)
            {
                if (member.Roles.Contains(premiumRole))
                {
                    //If they have the premium role, check if they should, and if they don't, revoke.
                    bool status = await UserManager.GetPremiumStatus(member.Id);
                    if (!status)
                    {
                        revoked++;
                        await member.RevokeRoleAsync(premiumRole);
                        if (member.Roles.Contains(premiumRoleColour))
                            await member.RevokeRoleAsync(premiumRoleColour);
                    }
                    else
                        fine++;
                }
                //Take a break between checking, don't want to hammer the server too much
                Task.Delay(50).Wait();
            }
            DiscordFollowupMessageBuilder responseBuilder = new()
            {
                Content = "Revoked premium for " + revoked + " users."
            };
            await ctx.FollowUpAsync(responseBuilder);
            return;
        }

        [SlashCommand("CheckAll", "Go through all users on the database, update their roles")]
        [RequireUserRole("Moderators")]
        public static async Task CheckAllCommand(InteractionContext ctx, [Option("Force", "Ignore expiration date? (Takes much longer)")] bool force)
        {
            DiscordInteractionResponseBuilder initResponseBuilder = new()
            {
                Content = "Checking users that may need the premium role removed. This can take a few minutes."
            };
            await ctx.CreateResponseAsync(initResponseBuilder);

            string connectionString = "Data Source=GBPremium.db;";

            List<ulong> userList = new();

            //Get all users in the database
            using (SqliteConnection connection = new(connectionString))
            {
                connection.Open();
                SqliteCommand? command = connection.CreateCommand();
                string comText = "SELECT UserID FROM Users";
                command.CommandText = comText;

                CryptoConfig? cfg = new();

                byte[] key = cfg.Key;
                byte[] iv = cfg.IV;

                using (SqliteDataReader? reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Stream idStream = reader.GetStream(0);
                        byte[] idData = new byte[idStream.Length];
                        idStream.Seek(0, SeekOrigin.Begin);
                        idStream.Read(idData, 0, idData.Length);

                        userList.Add(ulong.Parse(Crypto.DecryptStringFromBytes_Aes(idData, key, iv)));

                    }
                }
                connection.Close();
            }

            //Check each user's status. Wait 50ms between checks to save hammering the server.
            foreach (ulong userID in userList)
            {
                if (force)
                    await UserManager.UpdateUser(userID);
                else
                    await UserManager.GetPremiumStatus(userID);
                Task.Delay(50).Wait();
            }

            DiscordFollowupMessageBuilder responseBuilder = new()
            {
                Content = "Check completed!"
            };
            await ctx.FollowUpAsync(responseBuilder);
            return;
        }
    }
}
