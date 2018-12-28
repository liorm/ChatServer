using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace ChatClient
{
    class Msg
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        public string Name { get; set; }
        public string Text { get; set; }

        public override string ToString() => $"{Name}: {Text}";

        public static Msg Parse(byte[] buffer)
        {
            try
            {
                // Extract name size.
                var nameSize = BitConverter.ToUInt16(buffer, 0);

                // The rest is the text.
                var textSize = buffer.Length - nameSize - 2;

                // Size is invalid?
                if (textSize < 0)
                    return null;

                return new Msg()
                {
                    Name = Enc.GetString(buffer, 2, nameSize),
                    Text = Enc.GetString(buffer, 2 + nameSize, textSize)
                };
            }
            catch (Exception)
            {
                // Failed to parse buffer?
                return null;
            }
        }

        public byte[] Serialize()
        {
            var nameBuffer = Enc.GetBytes(Name);
            var textBuffer = Enc.GetBytes(Text);
            var result = new byte[2 + nameBuffer.Length + textBuffer.Length];

            // Write the name size.
            BitConverter.TryWriteBytes(result.AsSpan(), (ushort)nameBuffer.Length);

            // Copy the buffers.
            Buffer.BlockCopy(nameBuffer, 0, result, 2, nameBuffer.Length);
            Buffer.BlockCopy(textBuffer, 0, result, 2 + nameBuffer.Length, textBuffer.Length);

            return result;
        }
    }

    class Program
    {
        static async Task ClientCore(string myName, Func<int, string> getMsgCallback)
        {
            var client = new TcpClient();
            try
            {
                client.Connect(new IPEndPoint(IPAddress.Loopback, 5656));
            }
            catch (Exception e)
            {
                Log.LogError(e, "Failed to connect");
                return;
            }

            // Start background receive loop; close socket upon error.
            ReadLoopAsync(client).Forget(e => client.Close());

            var stream = client.GetStream();
            while (client.Connected)
            {
                var text = getMsgCallback(1000);
                if (text == null)
                    continue;

                var msg = new Msg()
                {
                    Name = myName,
                    Text = text
                };

                // Serialize and send.
                var buffer = msg.Serialize();

                try
                {
                    var msgSizeBuffer = BitConverter.GetBytes((ushort)buffer.Length);
                    await stream.WriteAsync(msgSizeBuffer, 0, msgSizeBuffer.Length).ConfigureAwait(false);
                    await stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Log and close socket.
                    Log.LogError(e, "Failed to send message");
                    client.Close();
                }
            }
        }

        private static async Task ReadLoopAsync(TcpClient client)
        {
            var stream = client.GetStream();

            var msgSizeBuffer = new byte[2];

            while (client.Connected)
            {
                var readSize = await stream.ReadAsync(msgSizeBuffer, 0, 2).ConfigureAwait(false);
                if (readSize != 2)
                    throw new InvalidOperationException($"Failed to read message size");

                var msgSize = BitConverter.ToUInt16(msgSizeBuffer, 0);

                // Read the message.
                var msgBuffer = new byte[msgSize];
                readSize = await stream.ReadAsync(msgBuffer, 0, msgBuffer.Length).ConfigureAwait(false);
                if (readSize != msgSize)
                    throw new InvalidOperationException($"Failed to read whole message (read: {readSize}, size: {msgSize})");

                var msg = Msg.Parse(msgBuffer);
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }
        }

        static void Main(string[] args)
        {
            var name = args.FirstOrDefault() ?? "no name";

            if (name == "--bot")
            {
                var count = int.Parse(args[1]);
                // TODO: Implement simulation mode.
            }
            else
            {
                // Run a single client
                ClientCore(name, timeout => Console.ReadLine()).Wait();
            }
        }
    }
}
