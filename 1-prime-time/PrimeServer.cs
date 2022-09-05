using System;
using System.Linq;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace PrimeTime
{
    public class PrimeServer
    {
        const int MAX_TASKS = 1024;

        public bool IsRunning { get; private set; } = false;

        public PrimeServer (string addr, int port)
        {
            _endpoint = new IPEndPoint (IPAddress.Parse (addr), port);
        }

        void AddTask(Action action)
        {
            Task t = new Task (action);
            while (true)
            {
                lock (_runningTasks)
                {
                    if (_runningTasks.Count == MAX_TASKS)
                    {
                        _runningTasks.RemoveAll (t => t.IsCompleted);
                        if (_runningTasks.Count == MAX_TASKS)
                            continue;
                    }

                    _runningTasks.Add (t);
                    t.Start ();
                    break;
                }
            }
        }

        void HandleClient (TcpClient client, CancellationToken ct)
        {
            foreach (var record in ReadRecords (client))
            {
                if (IsValidJson (record))
                {
                    Logger.Debug ($"Sending response to {client.Client.RemoteEndPoint}...");
                    var n = record!.RootElement.GetProperty ("number").GetDouble ();
                    var result = IsPrime (n);
                    SendResponse (client, result, ct);
                }
                else
                {
                    Logger.Debug ($"Sending MALFORMED response to {client.Client.RemoteEndPoint}...");
                    SendMalformedResponse (client, ct);

                    break;
                }
            }

            if (client.Connected)
            {
                Logger.Debug ($"Closing connection to {client.Client.RemoteEndPoint}...");
                client.Close ();
            }
        }

        public async Task Run (CancellationToken ct)
        {
            if (this.IsRunning)
                return;

            try
            {
                var server = new TcpListener (_endpoint);
                server.Start ();
                Logger.Info ($"Server listening for connections to {_endpoint}...");

                this.IsRunning = true;
                while (!ct.IsCancellationRequested)
                {
                    var client = await server.AcceptTcpClientAsync (ct);
                    if (client == null)
                        continue;

                    Logger.Debug ($"Accepted connection from {client.Client.RemoteEndPoint}");

                    AddTask (() => HandleClient (client, ct));
                }
            }
            catch (Exception ex)
            {
                Logger.Exception (ex);
            }
            finally
            {
                this.IsRunning = false;

                Task t = Task.WhenAll (_runningTasks);
                try { t.Wait (); }
                catch { }
            }
        }

        static bool IsPrime(double number)
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

            lock (_primeMemo)
            {
                if (_primeMemo.TryGetValue (number, out bool memo))
                    return memo;
            }

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

            lock (_primeMemo)
                _primeMemo[number] = result;

            return result;
        }

        void SendResponse (TcpClient client, bool isPrime, CancellationToken ct)
        {
            try
            {
                StreamWriter sw = new StreamWriter (client.GetStream ());
                var prime = isPrime ? "true" : "false";
                var response = $"{{ \"method\": \"isPrime\", \"prime\": {prime} }}";
                Logger.Debug ($"> {response}");
                sw.WriteLine (response);
                sw.Flush();
            }
            catch (Exception ex)
            {
                Logger.Exception (ex);
                client.Close ();
            }
        }

        void SendMalformedResponse (TcpClient client, CancellationToken ct)
        {
            try
            {
                StreamWriter sw = new StreamWriter (client.GetStream ());
                sw.WriteLine ("malformed request");
                sw.Flush ();
            }
            catch (Exception ex)
            {
                Logger.Exception (ex);
                client.Close ();
            }
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

        static IEnumerable<JsonDocument?> ReadRecords (TcpClient client)
        {
            StreamReader sr = new StreamReader (client.GetStream ());
            while (client.Connected)
            {
                string? line = sr.ReadLine ();
                if (string.IsNullOrEmpty(line))
                    yield break;

                Logger.Debug ($"> {line}");

                JsonDocument? result = null;
                try { result = JsonDocument.Parse (line); }
                catch (Exception ex) { Logger.Debug (ex.Message); }

                yield return result;
            }
        }

        readonly IPEndPoint _endpoint;

        readonly List<Task> _runningTasks = new List<Task> (MAX_TASKS);

        static readonly Dictionary<double, bool> _primeMemo = new Dictionary<double, bool> ();
    }
}