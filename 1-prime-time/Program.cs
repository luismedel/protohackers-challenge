
using CommandLine;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PrimeTime
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

        static void Run (Options o)
        {
            if (o.ShowTrace)
                Logger.AddTraceListener (new ConsoleTraceListener ());

            using (var cts = new CancellationTokenSource ())
            {
                var ct = cts.Token;

                Logger.Info ($"Binding server to {o.IpAddress}:{o.Port}...");
                PrimeServer server = new PrimeServer (o.IpAddress, o.Port);

                var t = StartServer (server, ct);

                Console.WriteLine ("Press [q] to quit the server...");

                while (server.IsRunning && Console.ReadKey ().KeyChar != 'q')
                    ;

                cts.Cancel ();
                t.Wait ();
            }
        }

        static Task StartServer (PrimeServer server, CancellationToken ct)
        {
            Task t = new Task (() => {
                try
                {
                    var awaiter = server.Run (ct).GetAwaiter ();
                    awaiter.GetResult ();
                }
                catch (OperationCanceledException)
                {
                    Logger.Info ($"Cancel requested.");
                }
                catch (Exception ex)
                {
                    Logger.Exception (ex);
                    throw;
                }
                finally
                {
                    Logger.Info ($"Server closed.");
                }
            }, ct);

            t.Start ();

            return t;
        }
    }
}