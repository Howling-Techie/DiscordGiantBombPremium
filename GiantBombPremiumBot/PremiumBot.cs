using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GiantBombPremiumBot
{
    public class PremiumBot
    {
        internal static EventId TestBotEventId { get; } = new EventId(1000, "TestBot");

        private BotConfig Config { get; }
        public DiscordClient Discord { get; }

        public PremiumBot(BotConfig cfg, int shardid)
        {
            // global bot config
            this.Config = cfg;

            // discord instance config and the instance itself
            var dcfg = new DiscordConfiguration
            {
                AutoReconnect = true,
                LargeThreshold = 250,
                MinimumLogLevel = LogLevel.Information,
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

            var slash = Discord.UseSlashCommands();
            slash.RegisterCommands<PremiumModule>(106386929506873344);
            slash.ContextMenuErrored += async (s, e) =>
            {
                if (e.Exception is ContextMenuExecutionChecksFailedException cmex)
                {
                    foreach (var check in cmex.FailedChecks)
                        if (check is RequireUserRole rol)
                        {
                            var response = new DiscordInteractionResponseBuilder();
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
            var depco = new ServiceCollection();

            Discord.ComponentInteractionCreated += Discord_ComponentInteractionCreated;

        }


        private async Task Discord_ComponentInteractionCreated(DiscordClient sender, ComponentInteractionCreateEventArgs e)
        {
            if (e.Id == "null")
            {
                return;
            }
            if (e.Id == "verify")
            {
                await Program.UpdateUser((DiscordMember)e.Interaction.User);
                bool premium = Program.IsUserPremium(e.Interaction.User);
                if(premium)
                {
                    DiscordInteractionResponseBuilder followup = new DiscordInteractionResponseBuilder();
                    followup.AsEphemeral(true);
                    followup.WithContent("You've got premium! <:Premium:852190261144453160> Congratulations!");
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                }
                else
                {
                    DiscordInteractionResponseBuilder followup = new DiscordInteractionResponseBuilder();
                    followup.AsEphemeral(true);
                    followup.WithContent("Weird, you don't have premium, did you do all of the above?");
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, followup);
                }
                //Program.userManager.WriteUserInfo();
            }
        }


        public async Task RunAsync()
        {
            var act = new DiscordActivity("people type", ActivityType.Watching);
            await this.Discord.ConnectAsync(act, UserStatus.Idle).ConfigureAwait(false);
        }

        public async Task StopAsync() => await this.Discord.DisconnectAsync().ConfigureAwait(false);

        private Task Discord_Ready(DiscordClient client, ReadyEventArgs e) => Task.CompletedTask;


        private Task Discord_SocketError(DiscordClient client, SocketErrorEventArgs e)
        {
            var ex = e.Exception is AggregateException ae ? ae.InnerException : e.Exception;
            client.Logger.LogError(TestBotEventId, ex, "WebSocket threw an exception");
            return Task.CompletedTask;
        }
    }
}