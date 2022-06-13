using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public override async Task<bool> ExecuteChecksAsync(ContextMenuContext ctx)
        {
            if (ctx.Guild.GetMemberAsync(ctx.User.Id).Result.Roles.Any(x => x.Name == RoleName))
                return true;
            else
                return false;
        }

    }
    class BlankModule : ApplicationCommandModule
    {

    }

    [SlashCommandGroup("Premium", "Commands for Giant Bomb Premium")]
    public class PremiumModule : ApplicationCommandModule
    {
        [SlashCommand("Connect", "Link your Discord account with Giant Bomb")]
        public async Task ConnectCommand(InteractionContext ctx)
        {
            await Program.UpdateUser(ctx.Member);
            bool premium = Program.IsUserPremium(ctx.User);
            if (premium)
            {
                DiscordInteractionResponseBuilder AlreadyPremiumResponse = new();
                AlreadyPremiumResponse.Content = "You're already premium!";
                AlreadyPremiumResponse.AsEphemeral(true);
                await ctx.CreateResponseAsync(AlreadyPremiumResponse);
                return;
            }
            string regCode = Program.userManager.GetUserVerifCode(ctx.User.Id);
            if (regCode == "" || regCode == null)
            {
                string URLString = "https://www.giantbomb.com/app/premiumdiscordbot/get-code?deviceID=dcb";
                XmlTextReader reader = new XmlTextReader(URLString);
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
                if (regCode == "" || regCode == null)
                {
                    DiscordInteractionResponseBuilder ErrorResponse = new();
                    ErrorResponse.Content = "Oops, something went wrong :(";
                    ErrorResponse.AsEphemeral(true);
                    await ctx.CreateResponseAsync(ErrorResponse);
                    return;
                }
            }
            Program.userManager.AddUser(ctx.User.Id, regCode);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = "Hi! To complete the process of linking your Giant Bomb account to your Discord account, just need to do a few quick things!" +
                "\n1. Visit the link below and enter the code **" + regCode + "**." +
                "\n2. Hit the big \"Hook me Up!\" button." +
                "\n3. Select \"Verify\" below, and we'll do the rest.";
            List<DiscordComponent> row0 = new List<DiscordComponent>
            {
                new DiscordLinkButtonComponent("https://www.giantbomb.com/app/premiumdiscordbot/activate", "giantbomb.com", false, new DiscordComponentEmoji(588743258130219010))
            };
            List<DiscordComponent> row1 = new List<DiscordComponent>
            {
                new DiscordButtonComponent(ButtonStyle.Primary, "verify", "Verify", false, new DiscordComponentEmoji(862738787918020609))
            };
            responseBuilder.AddComponents(row0);
            responseBuilder.AddComponents(row1);
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;

        }

        [SlashCommand("Recheck", "Link your Discord account with Giant Bomb")]
        public async Task RecheckCommand(InteractionContext ctx)
        {
            await Program.UpdateUser(ctx.Member);
            bool premium = await Program.UpdateUser(ctx.Member);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.IsEphemeral = true;
            responseBuilder.Content = premium ? "After rechecking, you are premium" : "After rechecking, you are not premium";
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Status", "Get your link status")]
        public async Task StatusCommand(InteractionContext ctx)
        {
            Console.WriteLine("Fetching status for " + ctx.Member.DisplayName);
            string status = await Program.userManager.GetStatus(ctx.User.Id);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = status;
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Unlink", "Remove the Giant Bomb account associated with this Discord account")]
        public async Task UnlinkCommand(InteractionContext ctx)
        {
            DiscordRole? premiumRole = null;
            DiscordRole? premiumRoleColour = null;
            //Search the server for the Premium and Primo roles
            foreach (var role in ctx.Member.Guild.Roles)
            {
                if (role.Value.Name == "Premium")
                    premiumRole = role.Value;
                else if (role.Value.Name == "Primo")
                    premiumRoleColour = role.Value;
            }
            if (ctx.Member.Roles.Contains(premiumRole))
            {
                await ctx.Member.RevokeRoleAsync(premiumRole);
            }
            if (ctx.Member.Roles.Contains(premiumRoleColour))
            {
                await ctx.Member.RevokeRoleAsync(premiumRoleColour);
            }
            Program.userManager.RemoveUser(ctx.Member.Id);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = "You've been removed from the system.";
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Remove", "Forcably unlink a user from their Giant Bomb account; useful for debugging")]
        [RequireUserRole("Moderators")]
        public async Task RemoveCommand(InteractionContext ctx, [Option("User", "User to remove")] DiscordUser user)
        {
            Program.userManager.RemoveUser(user.Id);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = user.Username + " has been removed from the system.";
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("CleanUp", "Go through all users on the server, and if they have premium, double check the validity")]
        [RequireUserRole("Moderators")]
        public static async Task CleanUpCommand(InteractionContext ctx)
        {
            var server = ctx.Guild;
            var roles = server.Roles;
            DiscordRole? premiumRole = null;
            DiscordRole? premiumRoleColour = null;
            int revoked = 0;
            int fine = 0;
            DiscordInteractionResponseBuilder initResponseBuilder = new();
            initResponseBuilder.Content = "Checking users that may need the premium role removed. This can take a few minutes.";
            initResponseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(initResponseBuilder);

            var members = await server.GetAllMembersAsync();

            foreach (var role in roles)
            {
                if (role.Value.Name == "Premium")
                    premiumRole = role.Value;
                if (role.Value.Name == "Primo")
                    premiumRoleColour = role.Value;
            }

            foreach (var member in members)
            {
                if (member.Roles.Contains(premiumRole))
                {
                    //If they have the premium role, check if they should, and if they don't, revoke.
                    bool status = await Program.userManager.GetPremiumStatus(member.Id);
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
            }
            DiscordFollowupMessageBuilder responseBuilder = new();
            responseBuilder.Content = "Revoked premium for " + revoked + " users.";
            responseBuilder.AsEphemeral(true);
            await ctx.FollowUpAsync(responseBuilder);
            return;
        }

        [SlashCommand("CheckAll", "Go through all users on the database, update their roles")]
        [RequireUserRole("Moderators")]
        public static async Task CheckAllCommand(InteractionContext ctx, [Option("Force", "Check user regardless of date due to expire")] bool force)
        {
            string connectionString = "Data Source=GBPremium.db;";

            int rowCount = 0;
            int usersChecked = 0;

            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                string comText = "SELECT COUNT(*) FROM Users";
                command.CommandText = comText;
                rowCount = Convert.ToInt32(command.ExecuteScalar());

                connection.Close();
            }
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = "Reprocessing users in the database...";
            responseBuilder.AsEphemeral(false);
            await ctx.CreateResponseAsync(responseBuilder);

            List<ulong> userList = new List<ulong>();

            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                string comText = "SELECT UserID FROM Users";
                command.CommandText = comText;

                var cfg = new CryptoConfig();

                byte[] key = cfg.Key;
                byte[] iv = cfg.IV;

                using (var reader = command.ExecuteReader())
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

            DiscordWebhookBuilder webhookBuilder = new DiscordWebhookBuilder();
            webhookBuilder.Content = "Reprocessing users in the database...\nChecked " + usersChecked + " of " + rowCount + ".";
            await ctx.EditResponseAsync(webhookBuilder);

            Task.Delay(1000).Wait();

            foreach (var userID in userList)
            {
                if (force)
                    await Program.userManager.UpdateUser(userID);
                else
                    await Program.userManager.GetPremiumStatus(userID);
                usersChecked++;
                Task.Delay(50).Wait();
                webhookBuilder.Content = "Reprocessing users in the database...\nChecked " + usersChecked + " of " + rowCount + ".";
                if (!force)
                {
                    if (usersChecked % 10 == 0)
                        await ctx.EditResponseAsync(webhookBuilder);
                }
                else
                    await ctx.EditResponseAsync(webhookBuilder);
            }

            webhookBuilder.Content = "Task completed!\nChecked " + usersChecked + " of " + rowCount + ".";
            await ctx.EditResponseAsync(webhookBuilder);
            return;
        }
    }
}
