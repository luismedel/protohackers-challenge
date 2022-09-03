using System;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace PrimeTime
{
    public class PrimeServer
    {
        public PrimeServer (string addr, int port)
        {
            _endpoint = new IPEndPoint (IPAddress.Parse (addr), port);
        }

        public async Task Run (CancellationToken ct)
        {
            var server = new TcpListener (_endpoint);
            server.Start ();
            Logger.Info ($"Server listening for connections to {_endpoint}...");

            while (!ct.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync (ct);
                if (client == null)
                    continue;

                Logger.Debug ($"Accepted connection from {client.Client.RemoteEndPoint}");

                await foreach (var record in ReadRecords(client, ct))
                {
                    if (IsValidJson (record))
                    {
                        Logger.Debug ($"Sending response to {client.Client.RemoteEndPoint}...");
                        var n = record!.RootElement.GetProperty ("number").GetDouble ();
                        await SendResponse (client, IsPrime(n), ct);
                    }
                    else
                    {
                        Logger.Debug ($"Sending MALFORMED response to {client.Client.RemoteEndPoint}...");
                        await SendMalformedResponse (client, ct);

                        Logger.Debug ($"Closing connection to {client.Client.RemoteEndPoint}...");
                        client.Close ();

                        break;
                    }
                }
            }
        }

        bool IsPrime (double number)
        {
            // Fractional numbers can't be prime
            if (Math.Floor (number) != number)
                return false;

            if (number < 2)
                return false;

            if (number == 2)
                return true;

            if (number % 2 == 0)
                return false;

            if (_primeMemo.TryGetValue (number, out bool memo))
                return memo;

            var limit = (int) Math.Floor (Math.Sqrt (number));

            var result = true;

            for (int i = 3; i <= limit; i += 2)
            {
                if (number % i == 0)
                {
                    result = false;
                    break;
                }
            }

            return result;
        }

        async Task SendResponse (TcpClient client, bool isPrime, CancellationToken ct)
        {
            StreamWriter sw = new StreamWriter (client.GetStream ());
            var prime = isPrime ? "true" : "false";
            var response = $"{{ \"method\": \"isPrime\", \"prime\": \"{prime}\" }}";
            Logger.Debug ($"> {response}");
            await sw.WriteLineAsync (response);
            await sw.FlushAsync ();
        }

        async Task SendMalformedResponse (TcpClient client, CancellationToken ct)
        {
            StreamWriter sw = new StreamWriter (client.GetStream ());
            await sw.WriteLineAsync ("malformed request");
            await sw.FlushAsync ();
        }

        bool IsValidJson (JsonDocument? json)
        {
            try
            {
                if (json == null)
                {
                    Logger.Debug ("> Non json.");
                    return false;
                }

                if (!json.RootElement.TryGetProperty ("method", out var method))
                {
                    Logger.Debug ("> Missing 'method' member.");
                    return false;
                }

                if (!method.ValueEquals ("isPrime"))
                {
                    Logger.Debug ("> method != 'isPrime'.");
                    return false;
                }

                if (!json.RootElement.TryGetProperty ("number", out var number))
                {
                    Logger.Debug ("> Missing 'number' member.");
                    return false;
                }

                if (!number.TryGetDouble (out var _))
                {
                    Logger.Debug ("> Not a valid number.");
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        static async IAsyncEnumerable<JsonDocument?> ReadRecords (TcpClient client, [EnumeratorCancellation] CancellationToken ct)
        {
            StreamReader sr = new StreamReader (client.GetStream ());
            while (client.Connected)
            {
                string? line = await sr.ReadLineAsync ();
                if (line == null)
                    yield break;

                Logger.Debug ($"> {line}");

                JsonDocument? result = null;
                try { result = JsonDocument.Parse (line); }
                catch { }

                yield return result;
            }
        }

        readonly IPEndPoint _endpoint;
        readonly Dictionary<double, bool> _primeMemo = new Dictionary<double, bool> ();
    }
}