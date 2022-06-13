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
        public static List<PremiumBot> Shards { get; } = new List<PremiumBot>();
        #endregion

        public static DateTime lastRun = new DateTime();
        public static DateTime nextRun = DateTime.UtcNow;

        public static Timer? timer = null;

        public static UserManager userManager = new UserManager();

        public static void Main(string[] args)
            => MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();


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
    TimeSpan.FromSeconds(10),
    TimeSpan.FromHours(1));


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


        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            foreach (var shard in Shards)
                shard.StopAsync().GetAwaiter().GetResult(); // it dun matter

            CancelTokenSource.Cancel();
        }

        internal static bool IsUserPremium(DiscordUser user)
        {
            return userManager.GetPremiumStatus(user.Id).Result;
        }
        public static async Task<bool> UpdateUser(DiscordMember member)
        {
            //Get User premium status
            return await userManager.UpdateUser(member.Id);
        }


        public static async void CheckAllUsers()
        {
            if (nextRun > DateTime.Now)
            {
                return;
            }

            await userManager.UpdateAllUsers();

            nextRun = userManager.GetNextCheckTime();
        }
    }
}