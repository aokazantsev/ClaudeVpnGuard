using System;
using System.Net.Sockets;
using System.Threading;

namespace ClaudeVpnGuard
{
    internal static class CorporateProbe
    {
        private const int TimeoutMs = 3000;

        public static void CheckAsync(string host, int port, Action<bool> done)
        {
            ThreadPool.QueueUserWorkItem(state => done(Check(host, port)));
        }

        private static bool Check(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult connecting = client.BeginConnect(host, port, null, null);
                    if (!connecting.AsyncWaitHandle.WaitOne(TimeoutMs)) return false;
                    client.EndConnect(connecting);
                    return client.Connected;
                }
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
