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
            Logger.Debug ($"Server listening for connections to {_endpoint}...");

            while (!ct.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync (ct);
                if (client == null)
                    continue;

                Logger.Debug ($"Accepted connection from {client.Client.RemoteEndPoint}");
                Logger.Indent ();

                try
                {
                    await EchoData (client, ct);
                }
                catch (Exception ex)
                {
                    Logger.Exception (ex);
                }
                finally
                {
                    if (client.Connected)
                    {
                        Logger.Debug ($"> Closing connection to {client.Client.RemoteEndPoint}.");

                        try { client.Close (); }
                        catch (Exception ex) { Logger.Exception (ex); }
                    }
                    else
                        Logger.Debug ($"> Client disconnected.");
                    
                    client.Dispose ();

                    Logger.Unindent ();
                }
            }
        }

        static async Task EchoData (TcpClient client, CancellationToken ct)
        {
            var buffer = new byte[1024];
            var stream = client.GetStream ();

            while (client.Connected)
            {
                int read = await stream.ReadAsync (buffer,0, buffer.Length, ct);
                if (read == 0)
                    break;

                Logger.Debug ($"> Received {read} bytes from {client.Client.RemoteEndPoint}");
                Logger.Debug ($"> [{BitConverter.ToString (buffer, 0, read)}]");

                await stream.WriteAsync (buffer, 0, read, ct);
            }
        }
        
        readonly IPEndPoint _endpoint;
    }
}