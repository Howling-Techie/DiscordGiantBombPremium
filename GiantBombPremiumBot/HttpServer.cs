using NetCoreServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using DSharpPlus.Entities;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace GiantBombPremiumBot
{
    class HttpDiscordSession : HttpSession
    {
        public HttpDiscordSession(NetCoreServer.HttpServer server) : base(server) { }

        protected override void OnReceivedRequest(HttpRequest request)
        {
            bool authorized = false;

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


            for (int i = 0; i < request.Headers; i++)
            {
                if (request.Header(i).Item1 == "Authorization")
                    if (request.Header(i).Item2 == "Bot " + cfg.Token)
                        authorized = true;
            }
            if (!authorized)
            {
                SendResponseAsync(Response.MakeErrorResponse(401));
                return;
            }

            string[] url = request.Url.Split('?');
            string value = request.Body;
            // Process HTTP request methods
            switch (request.Method)
            {
                case "HEAD":
                    SendResponseAsync(Response.MakeHeadResponse());
                    break;
                case "GET":
                    NameValueCollection keys;
                    switch (url[0])
                    {
                        case "/GuildMembers":
                            keys = HttpUtility.ParseQueryString(url[1]);
                            ulong guildID;
                            if (!ulong.TryParse(keys["guild"], out guildID))
                            {
                                SendResponseAsync(Response.MakeErrorResponse(400, "Request missing required parameters"));
                                break;
                            }
                            List<DiscordMember> members = Program.GetAllGuildMembers(guildID).Result;
                            if (members.Count > 0)
                            {
                                SendResponseAsync(Response.MakeGetResponse(JsonConvert.SerializeObject(members)));
                            }
                            else
                            {
                                SendResponseAsync(Response.MakeErrorResponse(405, "Access to Guild Denied"));
                            }
                            break;
                        case "/PremiumUsers":
                            List<User> users = UserManager.GetAllUsers();
                            //Scrub verif code from results
                            users.ForEach(u => u.verificationCode = String.Empty);
                            SendResponseAsync(Response.MakeGetResponse(JsonConvert.SerializeObject(users)));
                            break;
                        case "/PremiumUser":
                            keys = HttpUtility.ParseQueryString(url[1]);
                            ulong premiumUserID;
                            if (!ulong.TryParse(keys["user"], out premiumUserID))
                            {
                                SendResponseAsync(Response.MakeErrorResponse(400, "Request missing required parameters"));
                                break;
                            }
                            User? user = UserManager.GetUser(premiumUserID);
                            if (user != null)
                            {
                                user.verificationCode = String.Empty;
                                SendResponseAsync(Response.MakeGetResponse(JsonConvert.SerializeObject(user)));
                            }
                            else
                            {
                                SendResponseAsync(Response.MakeErrorResponse(404, "User Not Found"));
                            }
                            break;
                        default:
                            SendResponseAsync(Response.MakeErrorResponse(400, url[0] + " not a recognised GET command"));
                            break;
                    }
                    break;
                case "POST":
                    SendResponseAsync(Response.MakeErrorResponse(400, url[0] + " not a recognised POST command"));
                    break;
                case "PUT":
                    SendResponseAsync(Response.MakeErrorResponse(400, url[0] + " not a recognised PUT command"));
                    break;
                case "PATCH":
                    ulong userID;
                    switch (url[0])
                    {
                        case "/UpdatePremium":
                            keys = HttpUtility.ParseQueryString(url[1]);
                            if (!ulong.TryParse(keys["user"], out userID))
                            {
                                SendResponseAsync(Response.MakeErrorResponse(400, "Request missing required parameters"));
                                break;
                            }
                            User? user = UserManager.GetUser(userID);
                            if (user == null)
                            {
                                SendResponseAsync(Response.MakeErrorResponse(404, "User Not Found"));
                                break;
                            }
                            user.premiumStatus = UserManager.UpdateUser(userID).Result;
                            user.verificationCode = String.Empty;
                            SendResponseAsync(Response.MakeGetResponse(JsonConvert.SerializeObject(user)));
                            break;
                        case "/GivePremium":
                            ulong longDate;
                            keys = HttpUtility.ParseQueryString(url[1]);
                            if (!ulong.TryParse(keys["user"], out userID) || !ulong.TryParse(keys["add"], out longDate))
                            {
                                SendResponseAsync(Response.MakeErrorResponse(400, "Request missing required parameters"));
                                break;
                            }
                            bool giftResponse = UserManager.GiftUser(userID, longDate);
                            if(!giftResponse)
                                SendResponseAsync(Response.MakeErrorResponse(404, "User Not Found"));
                            else
                                SendResponseAsync(Response.MakeOkResponse());
                            break;
                        case "/RevokeGiftedPremium":
                            keys = HttpUtility.ParseQueryString(url[1]);
                            if (!ulong.TryParse(keys["user"], out userID))
                            {
                                SendResponseAsync(Response.MakeErrorResponse(400, "Request missing required parameters"));
                                break;
                            }
                            bool revokeResponse = UserManager.RevokeGiftUser(userID).Result;
                            if (!revokeResponse)
                                SendResponseAsync(Response.MakeErrorResponse(404, "User Not Found"));
                            else
                                SendResponseAsync(Response.MakeOkResponse());
                            break;
                        default:
                            SendResponseAsync(Response.MakeErrorResponse(400, url[0] + " not a recognised PATCH command"));
                            break;
                    }
                    break;
                case "DELETE":
                    SendResponseAsync(Response.MakeErrorResponse(400, url[0] + " not a recognised DELETE command"));
                    break;
                case "OPTIONS":
                    SendResponseAsync(Response.MakeOptionsResponse());
                    break;
                case "TRACE":
                    SendResponseAsync(Response.MakeTraceResponse(request.Cache.Data));
                    break;
                default:
                    SendResponseAsync(Response.MakeErrorResponse("Unsupported HTTP method: " + request.Method));
                    break;
            }
        }

        protected override void OnReceivedRequestError(HttpRequest request, string error)
        {
            Console.WriteLine($"Request error: {error}");
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"HTTP session caught an error: {error}");
        }
    }

    class HttpDiscordServer : NetCoreServer.HttpServer
    {
        public HttpDiscordServer(IPAddress address, int port) : base(address, port) { }

        protected override TcpSession CreateSession() { return new HttpDiscordSession(this); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"HTTP session caught an error: {error}");
        }
    }
}