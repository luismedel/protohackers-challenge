using System;
using System.Linq;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.ComponentModel;

namespace Protohackers
{
    public class Server
    {
        const int MAX_TASKS = 1024;
        const int ALLOC_TASK_RETRIES = 5;

        const int PACKET_SIZE = 9;

        public bool IsRunning { get; private set; } = false;

        public Server (string addr, int port)
        {
            _endpoint = new IPEndPoint (IPAddress.Parse (addr), port);
        }

        bool AddTask(Action action)
        {
            int retries = 0;

            Task t = new Task (action);
            while (true)
            {
                lock (_runningTasks)
                {
                    if (_runningTasks.Count == MAX_TASKS)
                    {
                        _runningTasks.RemoveAll (t => t.IsCompleted);
                        if (_runningTasks.Count == MAX_TASKS)
                        {
                            Logger.Warn ($"Max tasks reached ({MAX_TASKS})");
                            if (++retries < ALLOC_TASK_RETRIES)
                                continue;

                            return false;
                        }
                    }

                    _runningTasks.Add (t);
                    t.Start ();

                    return true;
                }
            }
        }

        void Insert (int timestamp, int price)
        {
            Logger.Info ($"Inserting {price} into {timestamp}");
            lock (_prices)
                _prices[timestamp] = price;
        }

        int Query (int tstart, int tend)
        {
            Logger.Info ($"Querying {tstart} <= T <= {tend}...");

            if (tstart > tend)
                return 0;

            int sum = 0;
            int count = 0;

            lock (_prices)
            {
                foreach (var kv in _prices)
                {
                    if (kv.Key < tstart)
                        continue;

                    if (kv.Key > tend)
                        break;

                    sum += kv.Key;
                    count++;
                }
            }

            return count != 0 ? (sum / count) : 0;
        }

        void HandleClient (TcpClient client, CancellationToken ct)
        {
            Logger.Indent ();
            
            foreach (var packet in ReadPackets(client, ct))
            {
                var arg1 = BitConverter.ToInt32 (packet, 1);
                var arg2 = BitConverter.ToInt32 (packet, 5);

                switch (packet[0])
                {
                    case (byte)'I': Insert (arg1, arg2); break;
                    case (byte) 'Q': WriteResponse (Query (arg1, arg2), client); break;

                    default:
                        Logger.Debug ($"Invalid request from {client.Client.RemoteEndPoint}");
                        break;
                }
            }

            Logger.Unindent ();

            if (client.Connected)
            {
                Logger.Debug ($"Closing connection to {client.Client.RemoteEndPoint}...");
                client.Close ();
            }
        }

        void WriteResponse(int response, TcpClient client)
        {
            var bytes = BitConverter.GetBytes (response);
            client.GetStream ().Write (bytes, 0, bytes.Length);
        }

        IEnumerable<byte[]> ReadPackets(TcpClient client, CancellationToken ct)
        {
            byte[] buffer = new byte[PACKET_SIZE];
            int offset = 0;

            while (client.Connected && !ct.IsCancellationRequested)
            {
                var read = client.GetStream ().Read (buffer, offset, PACKET_SIZE - offset);
                if (read == 0)
                {
                    Logger.Debug ($"No more data");
                    if (offset != 0)
                        Logger.Debug ($"Discarding {PACKET_SIZE - offset} bytes");
                    yield break;
                }

                if (read < PACKET_SIZE)
                {
                    Logger.Debug ($"Partial packet read: {read} bytes");
                    offset += read;
                    continue;
                }

                Logger.Debug ("Full packet read");

                yield return buffer;
                offset = 0;
            }
        }

        public async Task Run (CancellationToken ct)
        {
            if (this.IsRunning)
                return;

            this.IsRunning = true;

            var listener = new TcpListener (_endpoint);
            listener.Start ();

            Logger.Info ($"Listening for connections in {_endpoint}...");

            while (this.IsRunning && !ct.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync (ct);
                    if (client == null)
                        continue;

                    Logger.Debug ($"Accepted connection from {client.Client.RemoteEndPoint}");

                    if (!AddTask (() => HandleClient (client, ct)))
                    {
                        Logger.Error ($"Stopping server. Reason: Can't allocate new tasks");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Exception (ex);
                }
            }

            await this.EndTasks ();

            this.IsRunning = false;
        }

        async Task EndTasks()
        {
            var pending = _runningTasks.Where (t => !t.IsCompleted).ToArray ();
            if (pending.Length == 0)
                return;

            Logger.Info ($"Awaiting {pending.Length} tasks to complete...");

            await Task.WhenAll (pending);
        }

        public void Stop ()
        {
            this.IsRunning = false;
        }

        readonly SortedDictionary<int, int> _prices = new SortedDictionary<int, int> ();

        readonly IPEndPoint _endpoint;
        readonly List<Task> _runningTasks = new List<Task> (MAX_TASKS);
    }
}