
using CommandLine;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Protohackers
{
    class Program
    {
        public class Options
        {
            [Option ('a', "addr", Required = false, Default = "0.0.0.0", HelpText = "Address")]
            public string IpAddress { get; set; } = String.Empty;

            [Option ('p', "port", Required = false, Default = 7777, HelpText = "Port")]
            public int Port { get; set; }

            [Option ("trace", Required = false, Default = true, HelpText = "Show trace")]
            public bool ShowTrace { get; set; }
        }

        static void Main (string[] args)
        {
            Parser.Default
                  .ParseArguments<Options> (args)
                  .WithParsed<Options> (Run);
        }

        async static void Run (Options o)
        {
            if (o.ShowTrace)
                Logger.AddTraceListener (new ConsoleTraceListener ());

            using (var cts = new CancellationTokenSource ())
            {
                var ct = cts.Token;

                Logger.Info ($"Binding server to {o.IpAddress}:{o.Port}...");
                Server server = new Server (o.IpAddress, o.Port);

                RunServer (server, ct);
            }
        }

        static void RunServer (Server server, CancellationToken ct)
        {
            server.Run (ct)
                  .GetAwaiter ()
                  .GetResult ();
        }
    }
}