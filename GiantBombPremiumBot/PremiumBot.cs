using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace GiantBombPremiumBot
{
    public class PremiumBot
    {
        internal static EventId PremiumEventId { get; } = new EventId(1000, "PremiumBot");

        private BotConfig Config { get; }
        public DiscordClient Discord { get; }

        public PremiumBot(BotConfig cfg, int shardid)
        {
            // global bot config
            this.Config = cfg;

            // discord instance config and the instance itself
            DiscordConfiguration? dcfg = new()
            {
                AutoReconnect = true,
                LargeThreshold = 250,
                MinimumLogLevel = LogLevel.Warning,
                Token = this.Config.Token,
                TokenType = TokenType.Bot,
                ShardId = shardid,
                ShardCount = this.Config.ShardCount,
                MessageCacheSize = 2048,
                LogTimestampFormat = "dd-MM-yyyy HH:mm:ss zzz",
                Intents = DiscordIntents.All // if 4013 is received, change to DiscordIntents.AllUnprivileged
            };
            this.Discord = new DiscordClient(dcfg);

            // slash commands
            SlashCommandsExtension? slash = Discord.UseSlashCommands();
            //Server ID of the Giant Bomb discord, the commands will only work in this server
            //If this is ever needed for more servers, either clone the command with the other server ID, or remove the ID
            //number from the command below to allow the bot to fully function on all servers it is added to.
            //If the latter is done, commands need to be flushed.
            slash.RegisterCommands<PremiumModule>(106386929506873344);
            slash.ContextMenuErrored += async (s, e) =>
            {
                if (e.Exception is ContextMenuExecutionChecksFailedException cmex)
                {
                    foreach (ContextMenuCheckBaseAttribute? check in cmex.FailedChecks)
                        if (check is RequireUserRole rol)
                        {
                            DiscordInteractionResponseBuilder? response = new();
                            response.AsEphemeral(true);
                            response.WithContent($"Only <@{rol.RoleName}> users can run this command!");
                            await e.Context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, response);
                        }
                }
            };
            // events
            this.Discord.Ready += this.Discord_Ready;
            this.Discord.SocketErrored += this.Discord_SocketError;


            // build a dependency collection for commandsnext
            ServiceCollection? depco = new();

            Discord.ComponentInteractionCreated += Discord_ComponentInteractionCreated;

        }

        //Handler for when buttons are interacted with
        private async Task Discord_ComponentInteractionCreated(DiscordClient sender, ComponentInteractionCreateEventArgs e)
        {
            if (e.Id == "null")
            {
                //A general use "null" response to do nothing
                return;
            }
            if (e.Id == "verify")
            {
                //Runs when the user hits the "verify" button. Check that a user has premium, and let them know the response.
                bool premium = await UserManager.UpdateUser(e.Interaction.User.Id);
                if (premium)
                {
                    DiscordInteractionResponseBuilder followup = new();
                    followup.AsEphemeral(true);
                    followup.WithContent("You've got premium! <:Premium:852190261144453160> Congratulations!");
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                }
                else
                {
                    DiscordInteractionResponseBuilder followup = new();
                    followup.AsEphemeral(true);
                    followup.WithContent("Weird, you don't have premium, did you do all of the above?");
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                }
            }
            if (e.Id == "reset")
            {
                //If the user encounters an error while trying to link accounts they hit the reset button.
                //If they already have premium, let them know it worked, otherwise restart the proccess.
                bool premium = await UserManager.UpdateUser(e.Interaction.User.Id);
                if (premium)
                {
                    DiscordInteractionResponseBuilder followup = new();
                    followup.AsEphemeral(true);
                    followup.WithContent("You've already got premium! <:Premium:852190261144453160> Congratulations!");
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                }
                else
                {
                    UserManager.RemoveUser(e.Interaction.User.Id);

                    DiscordInteractionResponseBuilder followup = new();
                    followup.AsEphemeral(true);

                    string regCode = UserManager.GetUserVerifCode(e.Interaction.User.Id);
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
                            followup.WithContent("Oops, something went wrong :(");
                            await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                            return;
                        }
                    }
                    UserManager.AddUser(e.Interaction.User.Id, regCode);
                    followup.Content = "Okay, we've generated a new code! If you've still got any issues let us know in <#958417709446606869> and we'll see what we can do." +
                        "\n1. Visit the link below and enter the code **" + regCode + "**." +
                        "\n2. Hit the big \"Hook me Up!\" button." +
                        "\n3. Select \"Verify\" below, and we'll do the rest." +
                        "\n  If there's an issue verifying, hit \"Reset\" and we'll generate a new code.";
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
                    followup.AddComponents(row0);
                    followup.AddComponents(row1);
                    followup.AddComponents(row2);
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                    return;
                }
            }
        }


        public async Task RunAsync()
        {
            //Connect to the server
            DiscordActivity? act = new("over the premium users", ActivityType.Watching);
            await this.Discord.ConnectAsync(act, UserStatus.Online).ConfigureAwait(false);
        }

        public async Task StopAsync() => await this.Discord.DisconnectAsync().ConfigureAwait(false);

        private Task Discord_Ready(DiscordClient client, ReadyEventArgs e) => Task.CompletedTask;


        private Task Discord_SocketError(DiscordClient client, SocketErrorEventArgs e)
        {
            Exception? ex = e.Exception is AggregateException ae ? ae.InnerException : e.Exception;
            client.Logger.LogError(PremiumEventId, ex, "WebSocket threw an exception");
            return Task.CompletedTask;
        }
    }
}