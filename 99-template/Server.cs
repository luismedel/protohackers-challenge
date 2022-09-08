using System;
using System.Linq;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Protohackers
{
    public class Server
    {
        const int MAX_TASKS = 1024;
        const int ALLOC_TASK_RETRIES = 5;

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

        void HandleClient (TcpClient client, CancellationToken ct)
        {
            // Do the client handling here

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