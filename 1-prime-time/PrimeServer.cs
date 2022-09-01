﻿using System;
using System.Diagnostics;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace SmokeTest
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
            Trace.WriteLine ($"Server listening for connections to {_endpoint}...");

            while (!ct.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync (ct);
                if (client == null)
                    continue;

                Trace.WriteLine ($"Accepted connection from {client.Client.RemoteEndPoint}");

                await foreach (var record in ReadRecords(client, ct))
                {
                    if (IsValidJson (record))
                    {
                        Trace.WriteLine ($"Sending response to {client.Client.RemoteEndPoint}...");
                        var n = record!.RootElement.GetProperty ("number").GetDouble ();
                        await SendResponse (client, IsPrime(n), ct);
                    }
                    else
                    {
                        Trace.WriteLine ($"Sending MALFORMED response to {client.Client.RemoteEndPoint}...");
                        await SendMalformedResponse (client, ct);

                        Trace.WriteLine ($"Closing connection to {client.Client.RemoteEndPoint}...");
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
            await sw.WriteLineAsync ($"{{ \"method\": \"isPrime\", \"prime\": \"{prime}\" }}");
            await sw.FlushAsync ();
        }

        async Task SendMalformedResponse (TcpClient client, CancellationToken ct)
        {
            StreamWriter sw = new StreamWriter (client.GetStream ());
            await sw.WriteLineAsync ("{ \"error\": \"malformed request\" }");
            await sw.FlushAsync ();
        }

        bool IsValidJson (JsonDocument? json)
        {
            try
            {
                if (json == null)
                {
                    Trace.WriteLine ("> Non json.");
                    return false;
                }

                if (!json.RootElement.TryGetProperty ("method", out var method))
                {
                    Trace.WriteLine ("> Missing 'method' member.");
                    return false;
                }

                if (!method.ValueEquals ("isPrime"))
                {
                    Trace.WriteLine ("> method != 'isPrime'.");
                    return false;
                }

                if (!json.RootElement.TryGetProperty ("number", out var number))
                {
                    Trace.WriteLine ("> Missing 'number' member.");
                    return false;
                }

                if (!number.TryGetDouble (out var _))
                {
                    Trace.WriteLine ("> Not a valid number.");
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
            while (!sr.EndOfStream)
            {
                string? line = await sr.ReadLineAsync ();
                if (line == null)
                    yield break;

                JsonDocument? result = null;
                try { result = JsonDocument.Parse (line); }
                catch { }

                yield return result;
            }
        }

        static async Task SendData (TcpClient client, byte[] data, CancellationToken ct)
        {
            await client.GetStream ().WriteAsync (data, ct);
        }

        readonly IPEndPoint _endpoint;
        readonly Dictionary<double, bool> _primeMemo = new Dictionary<double, bool> ();
    }
}