using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
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
            string regCode = "";
            if(Program.IsUserRegistered(ctx.Member.Id))
            {
                regCode = Program.GetRegCode(ctx.Member.Id);
            }
            if(regCode == "")
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
                if (regCode == "")
                {
                    DiscordInteractionResponseBuilder ErrorResponse = new();
                    ErrorResponse.Content = "Oops, something went wrong :(";
                    ErrorResponse.AsEphemeral(true);
                    await ctx.CreateResponseAsync(ErrorResponse);
                    return;
                }
            }
            Program.AddUser(ctx, regCode);
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
            string status = await Program.GetStatus(ctx);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = status;
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }

        [SlashCommand("Unlink", "Remove the Giant Bomb account associated with this Discord account")]
        public async Task UnlinkCommand(InteractionContext ctx)
        {
            Program.RemoveUser(ctx.Member);
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
            Program.RemoveUser(user);
            DiscordInteractionResponseBuilder responseBuilder = new();
            responseBuilder.Content = user.Username + " has been removed from the system.";
            responseBuilder.AsEphemeral(true);
            await ctx.CreateResponseAsync(responseBuilder);
            return;
        }
    }
}
