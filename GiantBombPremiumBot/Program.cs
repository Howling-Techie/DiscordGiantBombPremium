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

using DSharpPlus.Entities;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace GiantBombPremiumBot
{
    public static class Program
    {
        #region Bot Info
        public static CancellationTokenSource CancelTokenSource { get; } = new CancellationTokenSource();
        private static CancellationToken CancelToken => CancelTokenSource.Token;
        public static List<PremiumBot> Shards { get; } = new List<PremiumBot>();
        #endregion

        static DateTime nextRun = DateTime.UtcNow;

        public static UserManager UserManager { get; set; } = new();

        public static void Main(string[] args)
            => MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();


#pragma warning disable IDE0060 // Remove unused parameter
        public static async Task MainAsync(string[] args)
#pragma warning restore IDE0060 // Remove unused parameter
        {
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
            Console.CancelKeyPress += Console_CancelKeyPress;
#pragma warning restore CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
            BotConfig? cfg = new();
            string? json = string.Empty;
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

            //Once an hour do the check in
            Timer timer = new(
    (e) => CheckAllUsers(),
    null,
    TimeSpan.FromSeconds(10),
    TimeSpan.FromHours(1));

            // MAYBE YOU WANT TO BE ABLE TO MONITOR THE BOT REMOTELY USING A PREMIUM BOT API AND WEB DASHBOARD? MUCH TO THINK ABOUT
            /* 
            // HTTP server port
            int port = 7070;

            Console.WriteLine($"HTTP server port: {port}");

            // Create a new HTTP server
            var server = new HttpDiscordServer(IPAddress.Any, port);

            // Start the server
            Console.Write("Server starting...");
            server.Start();
            Console.WriteLine("Done!");
            */

            List<Task>? tskl = new();
            if (cfg != null)
                for (int i = 0; i < cfg.ShardCount; i++)
                {
                    PremiumBot? bot = new(cfg, i);
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

            foreach (PremiumBot? shard in Shards)
                shard.StopAsync().GetAwaiter().GetResult(); // it dun matter

            CancelTokenSource.Cancel();
        }

        public static async void CheckAllUsers()
        {
            //If a run isn't due, do nothing
            if (nextRun > DateTime.Now)
            {
                return;
            }

            //Otherwise, check the users!
            await UserManager.UpdateAllUsers();

            nextRun = UserManager.GetNextCheckTime();
        }

        internal static async Task<List<DiscordMember>> GetAllGuildMembers(ulong guildID)
        {
            if (Shards[0].Discord.Guilds.ContainsKey(guildID))
            {
                return (await Shards[0].Discord.Guilds[guildID].GetAllMembersAsync()).ToList();
            }
            return new List<DiscordMember>();
        }
    }
}