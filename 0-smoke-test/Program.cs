
using CommandLine;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SmokeTest
{
    class Program
    {
        public class Options
        {
            [Option ('a', "addr", Required = false, Default = "127.0.0.1", HelpText = "Address")]
            public string IpAddress { get; set; } = String.Empty;

            [Option ('p', "port", Required = false, Default = 7777, HelpText = "Port")]
            public int Port { get; set; }

            [Option ("trace", Required = false, Default = true, HelpText = "Show trace")]
            public bool Trace { get; set; }
        }

        static void Main (string[] args)
        {
            Parser.Default
                  .ParseArguments<Options> (args)
                  .WithParsed<Options> (Run);
        }

        static void Run (Options o)
        {
            if (o.Trace)
                Trace.Listeners.Add (new ConsoleTraceListener ());

            using (var cts = new CancellationTokenSource ())
            {
                var ct = cts.Token;

                var t = RunServer (o.IpAddress, o.Port, ct);

                Console.WriteLine ("Press [q] to quit the server...");

                while (!ct.IsCancellationRequested && Console.ReadKey ().KeyChar != 'q')
                    ;

                cts.Cancel ();
                t.Wait ();
            }
        }

        static Task RunServer (string addr, int port, CancellationToken ct)
        {
            Task t = new Task (() => {
                EchoServer server = new EchoServer (addr, port);
                try
                {
                    Trace.WriteLine ($"Binding server to {addr}:{port}...");
                    var awaiter = server.Run (ct).GetAwaiter ();
                    awaiter.GetResult ();
                }
                catch (OperationCanceledException ex)
                {
                    Trace.WriteLine ($"Cancel requested.");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine ($"Error encountered: {ex.Message}");
                    throw;
                }
                finally
                {
                    Trace.WriteLine ($"Server closed.");
                }
            }, ct);
            t.Start ();
            return t;
        }
    }
}