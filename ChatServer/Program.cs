using System;
using Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace ChatServer
{
    class Server
    {
        public Server(ushort port = 5656)
        {
            m_port = port;
        }

        private readonly ushort m_port;

        class SocketHandler : IDisposable
        {
            public SocketHandler(Socket socket, Action<SocketHandler, byte[]> newMessageCallback, Action<SocketHandler> onErrorCallback)
            {
                m_socket = socket;
                m_newMessageCallback = newMessageCallback;
                m_onErrorCallback = onErrorCallback;
            }

            private readonly Socket m_socket;
            private readonly Action<SocketHandler, byte[]> m_newMessageCallback;
            private readonly Action<SocketHandler> m_onErrorCallback;

            private readonly ConcurrentQueue<byte[]> m_msgQueue = new ConcurrentQueue<byte[]>();
            private readonly ManualResetEventSlim m_msgAddedEvent = new ManualResetEventSlim();
            private RegisteredWaitHandle m_lastWaitResult;
            private bool m_disposed;

            /// <summary>
            /// Send a message on the socket.
            /// </summary>
            /// <param name="msg"></param>
            public void Enqueue(byte[] msg)
            {
                if (m_disposed)
                    return;

                m_msgQueue.Enqueue(msg);
                m_msgAddedEvent.Set();
            }

            private async void HandleMsgAdded(bool timedout = false)
            {
                // If socket is closed then there's nothing we can do but quit.
                if (!m_socket.Connected || m_disposed)
                    return;

                // Clear the event before attempting to dequeue messages.
                m_msgAddedEvent.Reset();

                // Dequeue messages. Limit the number of message to dequeue
                while (true)
                {
                    try
                    {
                        if (!m_msgQueue.TryDequeue(out var msg))
                            break;

                        // Send data on the socket.
                        var sentSize = await m_socket.SendAsync(msg, SocketFlags.None).ConfigureAwait(false);
                        
                        // Might be disposed after async operation completes.
                        if (m_disposed)
                            return;

                        if (sentSize != msg.Length)
                            throw new InvalidOperationException($"Failed to send whole message (sent: {sentSize}, size: {msg.Length})");
                    }
                    catch (Exception e)
                    {
                        // If disposed then ignore exception.
                        if (m_disposed)
                            return;

                        Log.LogError(e, "Failed to send data");

                        // Close socket upon error - this should trigger an exception on the "RunAsync" function - the "Main" handler logic for this socket.
                        m_socket.Close();
                    }
                }

                // Restart the wait.
                m_lastWaitResult = ThreadPool.RegisterWaitForSingleObject(m_msgAddedEvent.WaitHandle, (s, t) => HandleMsgAdded(t), null, 10000, true);
            }

            public async Task RunAsync()
            {
                // Start send loop.
                HandleMsgAdded();

                byte[] msgSizeBuffer = new byte[2];

                // Start socket receive loop.
                while (true)
                {
                    try
                    {
                        var readSize = await m_socket.ReceiveAsync(msgSizeBuffer, SocketFlags.None).ConfigureAwait(false);

                        // Might be disposed after async operation completes.
                        if (m_disposed)
                            return;

                        if (readSize != 2)
                            throw new InvalidOperationException($"Failed to read message size");

                        var msgSize = BitConverter.ToUInt16(msgSizeBuffer, 0);

                        // Read the message.
                        var msgBuffer = new byte[msgSize];
                        readSize = await m_socket.ReceiveAsync(msgBuffer, SocketFlags.None).ConfigureAwait(false);
                        
                        // Might be disposed after async operation completes.
                        if (m_disposed)
                            return;

                        if (readSize != msgSize)
                            throw new InvalidOperationException($"Failed to read whole message (read: {readSize}, size: {msgSize})");

                        // Broadcast the message.
                        m_newMessageCallback(this, msgBuffer);
                    }
                    catch (Exception e)
                    {
                        // If disposed then ignore exception.
                        if (m_disposed)
                            return;

                        Log.LogError(e, "Failed to receive data");

                        // Close socket upon error.
                        m_socket.Close();

                        // Notify the server of an error
                        m_onErrorCallback(this);

                        // Finish execution.
                        return;
                    }
                }
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;

                // Cancel wait for messages.
                m_lastWaitResult?.Unregister(m_msgAddedEvent.WaitHandle);

                // Close the socket.
                m_socket.Close();

                // Dispose of the objects.
                m_socket.Dispose();
                m_msgAddedEvent.Dispose();
            }
        }

        private readonly ConcurrentDictionary<SocketHandler, object> m_clients = new ConcurrentDictionary<SocketHandler, object>();

        public async Task RunAsync(CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Any, m_port);

            // Start listening.
            listener.Start();

            Log.LogDebug($"Started listening on port {m_port}");

            // Stop listening when cancelled.
            using (ct.Register(() => listener.Stop()))
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for connection.
                        var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
                        var handler = new SocketHandler(socket, MessageCallback, RemoveHandlerCallback);

                        // Add to the list of handlers.
                        m_clients.AddOrUpdate(handler, null, (k, v) => throw new InvalidOperationException());

                        Log.LogDebug($"Accepted a new client. Count: {m_clients.Count}");

                        // Handler loop - start in the background.
                        handler.RunAsync().ContinueWith(t => RemoveHandlerCallback(handler)).Forget(false);
                    }
                    catch (Exception e)
                    {
                        // Log and ignore the error
                        Log.LogError(e);
                    }
                }
            }
        }

        private void RemoveHandlerCallback(SocketHandler handler)
        {
            // Remove the dead handler.
            m_clients.TryRemove(handler, out var v);

            Log.LogDebug($"Removed a new client. Count: {m_clients.Count}");
        }

        private void MessageCallback(SocketHandler handler, byte[] msg)
        {
            // Broadcast the message to all other sockets.
            using (var enumerator = m_clients.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    // Ignore caller.
                    if (enumerator.Current.Key == handler)
                        continue;

                    enumerator.Current.Key.Enqueue(msg);
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            var pgm = new Server();

            // Catch termination signals.
            Console.CancelKeyPress += (o, v) => cts.Cancel();
            AppDomain.CurrentDomain.ProcessExit += (o, v) => cts.Cancel();

            // Run until terminated.
            pgm.RunAsync(cts.Token).Wait(cts.Token);
        }
    }
}
