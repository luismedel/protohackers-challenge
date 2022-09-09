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

                        Logger.Debug ($"Adding task {_runningTasks.Count}");
                        _runningTasks.Add (t);
                    }

                    t.Start ();

                    return true;
                }
            }
        }

        int Query (SortedList<int, int> prices, int tstart, int tend)
        {
            Logger.Info ($"Querying {tstart} <= T <= {tend}...");

            if (tstart > tend)
                return 0;

            int sum = 0;
            int count = 0;

            foreach (var item in prices)
            {
                if (item.Key < tstart)
                    continue;

                if (item.Key > tend)
                    break;

                sum += item.Value;
                count++;
            }

            if (prices.Count > 10000)
                Logger.Info ($"{sum}/{count} = {sum / count}");

            return count != 0 ? (sum / count) : 0;
        }

        void HandleClient (TcpClient client, CancellationToken ct)
        {
            Logger.Indent ();

            var prices = new SortedList<int, int> (1024);

            while (!ct.IsCancellationRequested)
            {
                foreach (var packet in ReadPackets (client, ct))
                {
                    Logger.Debug ($"{client.Client.RemoteEndPoint} {packet.command} {packet.arg1} {packet.arg2}");

                    switch (packet.command)
                    {
                        case 'I':
                            prices.TryAdd (packet.arg1, packet.arg2);
                            break;

                        case 'Q':
                            int result = Query (prices, packet.arg1, packet.arg2);
                            WriteResponse (result, client);
                            break;

                        default:
                            Logger.Debug ($"Invalid request from {client.Client.RemoteEndPoint}");
                            client.Close ();
                            continue;
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
                (byte)((response & 0xff000000) >> 24),
                (byte)((response & 0x00ff0000) >> 16),
                (byte)((response & 0x0000ff00) >> 8),
                (byte)(response & 0x000000ff),
            };

            client.GetStream ().Write (bytes, 0, 4);
        }

        IEnumerable<(int command, int arg1, int arg2)> ReadPackets(TcpClient client, CancellationToken ct)
        {
            byte[] buffer = new byte[PACKET_SIZE * 512];
            int prevRead = 0;

            while (client.Connected && !ct.IsCancellationRequested)
            {
                var read = client.GetStream ().Read (buffer, prevRead, buffer.Length - prevRead);
                if (read == 0)
                    break;

                int offset = 0;
                read += prevRead;
                while (read >= PACKET_SIZE)
                {
                    int cmd = buffer[offset++];

                    int arg1 = buffer[offset++] << 24
                             | buffer[offset++] << 16
                             | buffer[offset++] << 8
                             | buffer[offset++];

                    int arg2 = buffer[offset++] << 24
                             | buffer[offset++] << 16
                             | buffer[offset++] << 8
                             | buffer[offset++];

                    yield return new (cmd, arg1, arg2);

                    read -= PACKET_SIZE;
                }

                if (read != 0)
                {
                    prevRead = read;
                    Buffer.BlockCopy (buffer, offset, buffer, 0, read);
                }
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
                Logger.Info ($"Waiting for connections...");

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