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

        static void Insert (SortedDictionary<int, int> prices, int timestamp, int price)
        {
            Logger.Info ($"Inserting {price} into {timestamp}");
            prices[timestamp] = price;
        }

        int Query (SortedDictionary<int, int> prices, int tstart, int tend)
        {
            Logger.Info ($"Querying {tstart} <= T <= {tend}...");

            if (tstart > tend)
                return 0;

            int sum = 0;
            int count = 0;

            foreach (var kv in prices)
            {
                if (kv.Key < tstart)
                    continue;

                if (kv.Key > tend)
                    break;

                sum += kv.Key;
                count++;
            }

            return count != 0 ? (sum / count) : 0;
        }

        void HandleClient (TcpClient client, CancellationToken ct)
        {
            Logger.Indent ();

            SortedDictionary<int, int> prices = new SortedDictionary<int, int> ();

            while (client.Connected && !ct.IsCancellationRequested)
            {
                foreach (var packet in ReadPackets (client, ct))
                {
                    char cmd = (char) packet[0];
                    if (cmd != 'I' && cmd != 'Q')
                    {
                        Logger.Debug ($"Invalid request from {client.Client.RemoteEndPoint}");
                        client.Close ();
                        break;
                    }

                    int arg1 = packet[1] << 24 | packet[2] << 16 | packet[3] << 8 | packet[4];
                    int arg2 = packet[5] << 24 | packet[6] << 16 | packet[7] << 8 | packet[8];

                    Logger.Debug ($"Command: {cmd} {arg1} {arg2}");

                    switch (cmd)
                    {
                        case 'I': Insert (prices, arg1, arg2); break;
                        case 'Q': WriteResponse (Query (prices, arg1, arg2), client); break;
                    }
                }
            }

            Logger.Unindent ();

            if (client.Connected)
            {
                client.Close ();
            }
        }

        void WriteResponse(int response, TcpClient client)
        {
            byte[] bytes = {
                (byte)(response & 0xf000 >> 24),
                (byte)(response & 0x0f00 >> 16),
                (byte)(response & 0x00f0 >> 8),
                (byte)(response & 0x000f),
            };

            client.GetStream ().Write (bytes, 0, 4);
        }

        IEnumerable<byte[]> ReadPackets(TcpClient client, CancellationToken ct)
        {
            byte[] buffer = new byte[PACKET_SIZE];
            int offset = 0;

            while (client.Connected && !ct.IsCancellationRequested)
            {
                var read = client.GetStream ().Read (buffer, offset, PACKET_SIZE - offset);
                if (read == 0)
                    break;

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

            Logger.Debug ($"No more data");
            if (offset > 0)
            {
                if (PACKET_SIZE - offset == 0)
                    yield return buffer;
                else
                    Logger.Debug ($"Discarding {PACKET_SIZE - offset} bytes");
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

        readonly IPEndPoint _endpoint;
        readonly List<Task> _runningTasks = new List<Task> (MAX_TASKS);
    }
}