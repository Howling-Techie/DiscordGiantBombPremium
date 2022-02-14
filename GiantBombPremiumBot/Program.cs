// This file is part of the DSharpPlus project.
//
// Copyright (c) 2015 Mike Santiago
// Copyright (c) 2016-2021 DSharpPlus Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Security.Cryptography;
using DSharpPlus;
using DSharpPlus.Entities;
using System.Xml;
using System.Text;
using DSharpPlus.SlashCommands;

namespace GiantBombPremiumBot
{
    public static class Program
    {
        #region Bot Info
        public static CancellationTokenSource CancelTokenSource { get; } = new CancellationTokenSource();
        private static CancellationToken CancelToken => CancelTokenSource.Token;
        private static List<PremiumBot> Shards { get; } = new List<PremiumBot>();
        #endregion

        public static DateTime lastRun = new DateTime();
        public static DateTime nextRun = DateTime.UtcNow;

        public static Timer timer;

        public static UserManager userManager = new UserManager();

        public static void Main(string[] args)
            => MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();

        internal static bool IsUserRegistered(ulong userID)
        {
            return userManager.IsUserRegistered(userID);
        }

        internal static string GetRegCode(ulong userID)
        {
            return userManager.GetRegCode(userID);
        }

        public static async Task MainAsync(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            var cfg = new BotConfig();
            var json = string.Empty;
            if (!File.Exists("config.json"))
            {
                json = JsonConvert.SerializeObject(cfg);
                File.WriteAllText("config.json", json, new UTF8Encoding(false));
                Console.WriteLine("Config file was not found, a new one was generated. Fill it with proper values and rerun this program");
                Console.ReadKey();

                return;
            }

            json = File.ReadAllText("config.json", new UTF8Encoding(false));
            cfg = JsonConvert.DeserializeObject<BotConfig>(json);


            timer = new Timer(
    (e) => CheckAllUsers(),
    null,
    TimeSpan.FromSeconds(20),
    TimeSpan.FromHours(1));

            userManager.ReadUserInfo();

            var tskl = new List<Task>();
            for (var i = 0; i < cfg.ShardCount; i++)
            {
                var bot = new PremiumBot(cfg, i);
                Shards.Add(bot);
                tskl.Add(bot.RunAsync());
                await Task.Delay(7500).ConfigureAwait(false);
            }

            await Task.WhenAll(tskl).ConfigureAwait(false);

            try
            {
                await Task.Delay(-1, CancelToken).ConfigureAwait(false);
            }
            catch (Exception) { /* shush */ }

        }

        internal static void AddUser(InteractionContext ctx, string regCode)
        {
            userManager.AddUser(ctx.Member, regCode);
        }

        internal static void RemoveUser(DiscordUser member)
        {
            userManager.RemoveUser(member.Id);
        }

        internal static string GetStatus(InteractionContext ctx)
        {
            return userManager.GetStatus(ctx.User.Id);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            foreach (var shard in Shards)
                shard.StopAsync().GetAwaiter().GetResult(); // it dun matter

            CancelTokenSource.Cancel();
        }

        internal static bool IsUserPremium(DiscordUser user)
        {
            return userManager.IsUserPremium(user.Id);
        }

        public static async void UpdateUser(DiscordMember member)
        {
            if (userManager.IsUserRegistered(member.Id))
            {
                bool premium = userManager.UpdateUser(member.Id);

                DiscordRole? premiumRole = null;
                DiscordRole? premiumRoleColour = null;
                foreach (var role in member.Guild.Roles)
                {
                    if (role.Value.Name == "Premium")
                        premiumRole = role.Value;
                    else if (role.Value.Name == "Primo")
                        premiumRoleColour = role.Value;
                }
                if (premiumRole == null)
                {
                    premiumRole = await member.Guild.CreateRoleAsync("Premium");
                }
                if (premiumRoleColour == null)
                {
                    premiumRoleColour = await member.Guild.CreateRoleAsync("Primo");
                }
                if (premium)
                {
                    await member.GrantRoleAsync(premiumRole);
                    //await member.GrantRoleAsync(premiumRoleColour);
                }
                else if (member.Roles.Contains(premiumRole))
                {
                    await member.RevokeRoleAsync(premiumRole);
                    await member.RevokeRoleAsync(premiumRoleColour);
                }
            }
        }


        public static async void CheckAllUsers()
        {
            if (nextRun > DateTime.Now)
            {
                return;
            }

            List<DiscordGuild> guilds = new();
            foreach (var shard in Shards)
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
                foreach (var user in userManager.users)
                {
                    if (userManager.IsUserRegistered(user.Key) && guild.Members.ContainsKey(user.Key))
                    {
                        var guildUser = guild.Members[user.Key];
                        bool premiumStatus = userManager.IsUserPremium(user.Key);
                        if (premiumStatus)
                        {
                            await guildUser.GrantRoleAsync(premiumRole);
                            //await user.Value.GrantRoleAsync(premiumRoleColour);
                        }
                        else if (guildUser.Roles.Contains(premiumRole))
                        {
                            await guildUser.RevokeRoleAsync(premiumRole);
                            await guildUser.RevokeRoleAsync(premiumRoleColour);
                        }
                    }
                }
            }
            nextRun = userManager.GetNextCheckTime();
        }
    }
}