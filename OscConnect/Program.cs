﻿using MeaMod.DNS.Multicast;
using Newtonsoft.Json;
using NPSMLib;
using SharpOSC;
using Spectre.Console;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VRC.OSCQuery;
using Extensions = VRC.OSCQuery.Extensions;

namespace OscConnect
{
    internal class Program
    {

        public static string Name = "OscConnect";
        public static string Ver = "1.1";
        class Settings{
            public bool ChatboxMsg = true;
            public string defaultapp = "";
            public int recieveport = 9001;
            public int updatems = 3000;
            public string lastver = Ver;
        }

        static Settings currsettings = new Settings();

        static class Data
        {
            public static string Title = "";
            public static string Artist = "";
            public static bool Playing = false;
            public static double CurrTime = 0;
            public static double EndTime = 0;
        }

        private static void Log(string msg)
        {
            Console.WriteLine(msg);
        }

        private static void Setup()
        {
            if (!File.Exists("Settings.json"))
            {
                File.Create("Settings.json").Close();
                File.WriteAllText("Settings.json", JsonConvert.SerializeObject(new Settings()));
                Log("Created settings file");
            }
            try
            {
                var jsonbuildsettings = new JsonSerializerSettings();
                jsonbuildsettings.NullValueHandling = NullValueHandling.Include;
                jsonbuildsettings.Formatting = Formatting.Indented;
                var des = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("Settings.json"), jsonbuildsettings);
                Log("Last launched ver: " + des.lastver);

                if (des.lastver != Ver || des.lastver == "" || des.lastver == null)
                {
                    Log("Rebuilding settings file");
                    File.Delete("Settings.json");
                    Setup();
                }
                currsettings = des;
            }
            catch
            {
                Log("Invalid Settings!");
                File.Delete("Settings.json");
                Setup();
            }
        }

        static void PlayerUpdate(MediaPlaybackDataSource ds)
        {
            var songdata = ds.GetMediaObjectInfo();
            var playdata = ds.GetMediaPlaybackInfo();
            var timeline = ds.GetMediaTimelineProperties();
            Data.Title = songdata.Title;
            Data.Artist = songdata.Artist;
            Data.Playing = playdata.PlaybackState.ToString() == "Playing" && playdata.PlaybackState.ToString() != "Paused";
            Data.CurrTime = timeline.Position.TotalMilliseconds;
            Data.EndTime = timeline.EndTime.TotalMilliseconds;
        }
        public static string LimitLength(string source, int maxLength)
        {
            if (source.Length <= maxLength)
            {
                return source;
            }

            return source.Substring(0, maxLength);
        }
        private static string CenterString(string s, int width)
        {
            if (s.Length >= width)
            {
                return s;
            }

            int leftPadding = (width - s.Length) / 2;
            int rightPadding = width - s.Length - leftPadding;

            return new string('\u2060', leftPadding) + s + new string('\u2060', rightPadding);
        }
        private static void ChatMsg(SharpOSC.UDPSender sender, string[] msgs)
        {
            string totalmessage = "";
            int limit = 32;
            string newline = " \v ";
            int currmsg = 0;
            foreach (var startmsg in msgs)
            {
                currmsg += 1;
                string msg = startmsg;
                if (msg.Length >= limit)
                {
                    totalmessage += CenterString(LimitLength(msg, limit - 3) + "...", limit);

                }
                else
                {
                    totalmessage += msg;

                }
                if (msgs.Count() > currmsg)
                {
                    totalmessage += newline;

                }
            }
            sender.Send(new SharpOSC.OscMessage("/chatbox/input", totalmessage, true, false));
        }
        private static string Symbol(bool condition, string tru, string fal)
        {
            if (condition == true)
            {
                return tru;
            }
            else
            {
                return fal;

            }
        }
        public class Chars
        {
            public static string play = @"▶";
            public static string pause = @"⏸";
            public static string Explicit = @"🅴";
            public static string Shaded = @"█";
            public static string UnShaded = @"░";
            public static string Note = @"🎵";
            public static string Blank = "\u2060";
        }
        private static string Progress(double progress, int bars)
        {

            string barstring = "";
            char[] partChar = { ' ', '▏', '▎', '▍', '▌', '▋', '▊', '█' };

            for (int i = 0; i < bars; i += 1)
            {
                barstring = "";
                double ma = (progress / 100) * (bars);
                double full = Math.Floor(ma);

                for (int v = 0; v < full; v += 1)
                {
                    barstring += partChar[7];
                }

                double part = Math.Floor((double)((decimal)ma % 1) * partChar.Count());
                barstring += partChar[(int)part];
                for (int c = 0; c < (bars - 1) - full; c += 1)
                {
                    barstring += Chars.Blank;
                }
            }
            return barstring;
        }


        static void Main(string[] args)
        {
            Console.Title = $"{Name} v{Ver}";

            Log($"Welcome to {Name} v{Ver}");
            Setup();
            NPSMLib.NowPlayingSessionManager npsm = new NowPlayingSessionManager();
            if (npsm.Count < 1)
            {
                Log("Start an app that supports NowPlayingSessions first!");
                Console.Read();
                return;
            }
            List<string> currsessions = new List<string>();
            foreach (var sess in npsm.GetSessions())
            {
                currsessions.Add(Process.GetProcessById((int)sess.PID).ProcessName);
            }
            var app = currsettings.defaultapp;
            if (currsettings.defaultapp.Length < 1)
            {
                 app = AnsiConsole.Prompt(

     new SelectionPrompt<string>()
 .Title("Choose an app to bind to")
    .PageSize(10)
    .MoreChoicesText("[grey](Move up and down)[/]")
    .AddChoices(currsessions)
    .EnableSearch()

);
            }
            var selectedsess = npsm.GetSessions()[currsessions.IndexOf(app)];
            AnsiConsole.WriteLine($"Binded to {app}");
            var data = selectedsess.ActivateMediaPlaybackDataSource();
            PlayerUpdate(data);
            data.MediaPlaybackDataChanged += delegate (object sender, MediaPlaybackDataChangedArgs e)
            {
                PlayerUpdate(e.MediaPlaybackDataSource);
            };
            var tcpPort = Extensions.GetAvailableTcpPort();
            var udpPort = Extensions.GetAvailableUdpPort();

            var Query = new OSCQueryServiceBuilder()
                .WithTcpPort(tcpPort)
                .WithUdpPort(udpPort)
                .WithServiceName(Name)
                .AdvertiseOSCQuery()
                .AdvertiseOSC()
                .Build();
            Query.AddEndpoint<string>("/chatbox/input", Attributes.AccessValues.ReadWrite);
            Query.AddEndpoint<int>("/avatar/parameters/OscConnectNext", Attributes.AccessValues.WriteOnly);
            Query.AddEndpoint<int>("/avatar/parameters/OscConnectPrevious", Attributes.AccessValues.WriteOnly);
            Query.AddEndpoint<int>("/avatar/parameters/OscConnectRewind", Attributes.AccessValues.WriteOnly);
            Query.AddEndpoint<int>("/avatar/parameters/OscConnectFF", Attributes.AccessValues.WriteOnly);
            Query.AddEndpoint<int>("/avatar/parameters/OscConnectToggleState", Attributes.AccessValues.WriteOnly);

            Log("Finding vrc to connect to");
            int sendport = 0;
            IPAddress sendaddress = null;
            bool GetVrc()
            {
                foreach(var service in Query.GetOSCServices())
                {
                    Log("Found vrc at port " + service.port + " ip " + service.address.ToString());
                    sendport = service.port;
                    sendaddress = service.address;
                    return true;
                }
                return false;
            }
            Log("Waiting for vrc (make sure the game is open and osc is enabled)");
            while (!GetVrc())
            {
                Thread.Sleep(5000);
            }
            Query.StartHttpServer();
            Log($"Started local osc server tcp {tcpPort} udp {udpPort} (http://localhost:{tcpPort})");
            HandleOscPacket callback = delegate (OscPacket packet)
            {
                
                var messageReceived = (OscMessage)packet;
                if (messageReceived != null)
                {
                    switch (messageReceived.Address)
                    {
                        case "/avatar/parameters/OscConnectNext":
                            if ((int)messageReceived.Arguments[0] == 1)
                            {
                                data.SendMediaPlaybackCommand(MediaPlaybackCommands.Next);
                            }
                            break;
                        case "/avatar/parameters/OscConnectPrevious":
                            if ((int)messageReceived.Arguments[0] == 1)
                            {
                                data.SendMediaPlaybackCommand(MediaPlaybackCommands.Previous);
                            }
                            break;
                        case "/avatar/parameters/OscConnectRewind":
                            if ((int)messageReceived.Arguments[0] == 1)
                            {
                                data.SendPlaybackPositionChangeRequest(data.GetMediaTimelineProperties().Position - TimeSpan.FromSeconds(10));
                            }
                            break;
                        case "/avatar/parameters/OscConnectFF":
                            if ((int)messageReceived.Arguments[0] == 1)
                            {
                                data.SendPlaybackPositionChangeRequest(data.GetMediaTimelineProperties().Position + TimeSpan.FromSeconds(10));
                            }
                            break;
                        case "/avatar/parameters/OscConnectToggleState":
                            if ((int)messageReceived.Arguments[0] == 1)
                            {
                                data.SendMediaPlaybackCommand(MediaPlaybackCommands.PlayPauseToggle);
                            }
                            break;
                        default:
                            break;
                    }

                }
            };
            new UDPListener(udpPort, callback);
            int updates = 2;
            int stages = 2;
            int stage = 1;
            if (currsettings.ChatboxMsg)
            {
                Log("Start chatbox");
                var oscsender = new SharpOSC.UDPSender(sendaddress.ToString(), sendport);
                while (Thread.CurrentThread.ThreadState == System.Threading.ThreadState.Running)
                {
                    for (int a = 0; a < updates; a++)
                    {
                        switch (stage)
                        {
                            case 1:
                                ChatMsg(oscsender, new string[] { $"{Symbol(Data.Playing, Chars.pause, Chars.play)}      {Data.Title}", $"{Data.Artist}", $"{TimeSpan.FromMilliseconds((double)Data.CurrTime).ToString(@"mm\:ss")} [{Progress((float)(Data.CurrTime / Data.EndTime) * 100, 6)}] {TimeSpan.FromMilliseconds((double)Data.EndTime).ToString(@"mm\:ss")}" });

                                break;

                            case 2:
                                ChatMsg(oscsender, new string[] { $"🔗 Osc Connect {Ver}", $"Binded to {app}", $"Local time: {DateTime.Now.ToString("hh:mm tt")}" });
                                break;

                            default:
                                Log("ERROR");
                                break;
                        }
                        Thread.Sleep(currsettings.updatems);
                    }
                    stage++;
                    if (stage > stages)
                    {
                        stage = 1;
                    }
                }
            }
            Thread.Sleep(-1);
        }

    } 
}
