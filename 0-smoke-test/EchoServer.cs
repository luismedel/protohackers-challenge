using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SmokeTest
{
    public class EchoServer
    {
        public EchoServer (string addr, int port)
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

                await EchoData (client, ct);

                //await SendData (client, data, ct);
                //Trace.WriteLine ($"> Sent {data.Length} bytes to {client.Client.RemoteEndPoint}");

                Trace.WriteLine ($"> Closing connection to {client.Client.RemoteEndPoint}.");
                client.Close ();
            }
        }

        static async Task EchoData (TcpClient client, CancellationToken ct)
        {
            var buffer = new byte[1024];
            var stream = client.GetStream ();

            while (stream.DataAvailable)
            {
                int read = await stream.ReadAsync (buffer, ct);
                Trace.WriteLine ($"> Received {read} bytes from {client.Client.RemoteEndPoint}");
                Trace.WriteLine ($"> [{BitConverter.ToString (buffer, 0, read)}]");
                await stream.WriteAsync (buffer, 0, read);
            }
        }

        static async Task SendData (TcpClient client, byte[] data, CancellationToken ct)
        {
            await client.GetStream ().WriteAsync (data, ct);
        }

        readonly IPEndPoint _endpoint;
    }
}