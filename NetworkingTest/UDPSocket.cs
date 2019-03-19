using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace NetworkingTest {
    class UDPSocket {
        private Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private const int bufSize = 8 * 1024;
        private State state = new State();
        private EndPoint epFrom = new IPEndPoint(IPAddress.Any, 0);
        private AsyncCallback recv = null;

        public delegate void ReceivedMessage(string fromIP, string message);
        /// <summary>
        /// Event called when received a message.
        /// </summary>
        public event ReceivedMessage OnReceivedUDPMessage;

        public class State {
            public byte[] buffer = new byte[bufSize];
        }

        public void Server(string address, int port) {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
            Receive();
        }

        public void Client(string address, int port) {
            socket.Connect(IPAddress.Parse(address), port);
            //Receive();
        }

        public void Send(string text) {
            byte[] data = Encoding.ASCII.GetBytes(text);

            socket.BeginSend(data, 0, data.Length, SocketFlags.None, delegate (IAsyncResult ar) {
                State so = (State)ar.AsyncState;
                int bytes = socket.EndSend(ar);
                Console.WriteLine($"Send {bytes}, {text}");
            }, state);
        }

        private void Receive() {
            socket.BeginReceiveFrom(state.buffer, 0, bufSize, SocketFlags.None, ref epFrom, recv =
                delegate (IAsyncResult ar) {
                    State so = (State)ar.AsyncState;
                    int bytes = socket.EndReceiveFrom(ar, ref epFrom);
                    socket.BeginReceiveFrom(so.buffer, 0, bufSize, SocketFlags.None, ref epFrom, recv, so);
                    Console.WriteLine($"RECV {epFrom.ToString()}: {bytes} " +
                        $"{Encoding.ASCII.GetString(so.buffer, 0, bytes)}");

                    OnReceivedUDPMessage?.Invoke(epFrom.ToString(),
                        Encoding.ASCII.GetString(so.buffer, 0, bytes));
                }, state);

        }

        /// <summary>
        /// Disconnects the socket and allows reuse of the socket afterwards
        /// </summary>
        public void DisconnectSocket() {
            socket.Disconnect(true);
        }
    }
}
