using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using BogdanCodreanu.Network.Utilities;

namespace BogdanCodreanu.Network {
    class NetworkServer {
        /// <summary>
        /// How many miliseconds for a client to stay silet to be considered disconnected.
        /// </summary>
        private int timeoutWithoutClientResponse = 5000; // miliseconds

        /// <summary>
        /// Should the server automatically handle when to listen for new connections?
        /// It will always listen when there is more room for a new connection.
        /// Listening is turned off after max connections have been formed (independently of this value)
        /// </summary>
        private bool autoStartListening;

        private string ipAddress;
        private int tcpListenPort;
        private int udpPort;
        private int maxConnections;
        public List<PortForClient> Ports { get; private set; }

        private TcpListener tcpListener;
        private bool ListeningForNewConnections => tcpListener != null;

        private UdpClient udpReceiver;
        private bool serverOn = false;

        public class PortForClient {
            /// <summary>
            /// Port number
            /// </summary>
            public int portno;
            /// <summary>
            /// Is port active? Does it have a client on the other side?
            /// </summary>
            public bool isUsed;
            public TcpClient tcpClient;
            public NetworkStream networkStream;

            public override string ToString() {
                return $"[{portno}" + (isUsed ? " closed. In use" : " open. Not used") + "]";
            }
        }

        /// <summary>
        /// Event called when printing status of the connection.
        /// </summary>
        public event MyNetworkUtilities.ErrorLog OnLogStatus;
        /// <summary>
        /// Event called when server is started.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnStartedServer;
        /// <summary>
        /// Event called when server is stopped
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnStoppedServer;
        /// <summary>
        /// Event called when tcp listener is started.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnStartedListener;
        /// <summary>
        /// Event called when tcp listener has stopped.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnStoppedListener;
        /// <summary>
        /// Event called when a client has connected to the server.
        /// </summary>
        public event MyNetworkUtilities.ServerConnectionEvent OnClientConnected;
        /// <summary>
        /// Event called when a client has disconnected from the server.
        /// </summary>
        public event MyNetworkUtilities.ServerConnectionEvent OnClientDisonnected;
        /// <summary>
        /// Event called when the server has received a message via TCP from a client.
        /// </summary>
        public event MyNetworkUtilities.ServerRecvTCPMessageEvent OnTCPClientReceivedMessage;
        /// <summary>
        /// Event called when the server has received a message from UDP.
        /// </summary>
        public event MyNetworkUtilities.ServerRecvUDPMessageEvent OnUDPReceivedMessage;

        /// <summary>
        /// Constructor of server
        /// </summary>
        /// <param name="timeoutSecondsWithoutResponseClient">Time in seconds to tiemout a client connection
        /// if no message from the client is received (including pings)</param>
        /// <param name="autoStartListening">Should the server automatically handle when to listen 
        /// for new connections? It will always listen when there is more room for a new connection.
        /// If true, server will always start listening if there is room for a new connection (even if you
        /// stopped listening manually)
        /// Listening is turned off after max connections have been formed (independently of this value)</param>
        public NetworkServer(int timeoutSecondsWithoutResponseClient, bool autoStartListening = true) {
            timeoutWithoutClientResponse = timeoutSecondsWithoutResponseClient * 1000;
            this.autoStartListening = autoStartListening;
        }

        /// <summary>
        /// Starts the server. Also starts listening for incoming connections.
        /// </summary>
        /// <param name="ipAddress">The ip address of this machine. Should be local ip</param>
        /// <param name="udpPort">The port where udp data is going to be received</param>
        /// <param name="portListenForConnections">The port where we listen for new connections</param>
        /// <param name="tcpPortStart">The first port where we're going to place tcp connections</param>
        /// <param name="maxConnections">Maximum connections</param>
        public void StartServer(string ipAddress, int udpPort, int portListenForConnections,
            int tcpPortStart, int maxConnections) {
            if (serverOn)
                return;

            this.ipAddress = ipAddress;
            this.maxConnections = maxConnections;

            Ports = new List<PortForClient>();
            for (int i = 0; i < maxConnections; i++) {
                int port = tcpPortStart + i;
                Ports.Add(new PortForClient() { isUsed = false, portno = port });
            }

            tcpListenPort = portListenForConnections;
            this.udpPort = udpPort;
            udpReceiver = new UdpClient(new IPEndPoint(IPAddress.Parse(ipAddress), udpPort));

            StartListeningForConnections();
            serverOn = true;
            ReceiveUdpMessages();

            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, $"Server succesfully started.");
            OnStartedServer?.Invoke();
        }

        private PortForClient GetFirstPortAvaliable() {
            foreach (PortForClient port in Ports) {
                if (!port.isUsed) {
                    return port;
                }
            }
            return null;
        }

        /// <summary>
        /// How many ports are currently used?
        /// </summary>
        private int GetNrOfPortsUsed() {
            int result = 0;
            foreach (PortForClient port in Ports) {
                if (port.isUsed)
                    result++;
            }
            return result;
        }
        /// <summary>
        /// How many ports are currently avaliable?
        /// </summary>
        private int NrOfPortsAvaliable => maxConnections - GetNrOfPortsUsed();

        /// <summary>
        /// Starts listening for incoming connections
        /// </summary>
        public async void StartListeningForConnections() {
            if (ListeningForNewConnections) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Listener is already listening.");
                return;
            }


            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), tcpListenPort);
            tcpListener = new TcpListener(endPoint);

            tcpListener.Start();
            OnStartedListener?.Invoke();
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Listener for incoming connections started");

            while (NrOfPortsAvaliable > 0) {
                // listen for an incoming connection
                TcpClient heardClient;
                try {
                    heardClient = await tcpListener.AcceptTcpClientAsync();
                } catch (ObjectDisposedException) {
                    //object was disposed
                    // meaning StopListening was called from outside
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                        $"Listener forced closed");
                    return;
                } catch (SocketException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Error,
                        $"Listener socket closed");
                    return;
                }
                // connection heard

                // create a stream to send data 
                NetworkStream ns = heardClient.GetStream();

                // get avaliable port
                PortForClient avaliablePort = GetFirstPortAvaliable();
                if (avaliablePort != null) {
                    // tell the listened client to connect to another port.
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                        $"Incoming connection heard. Directing it to port {avaliablePort}");
                    byte[] sendData = Encoding.ASCII.GetBytes($"Connect to this port:{avaliablePort.portno}:{udpPort}");
                    ns.Write(sendData, 0, sendData.Length);
                    avaliablePort.isUsed = true;
                    WaitConnectionOn(avaliablePort);
                }

                ns.Close();
                heardClient.Close();
            }
            StopListening();
        }

        /// <summary>
        /// Stops listening for incoming connections
        /// </summary>
        public void StopListening() {
            if (!ListeningForNewConnections)
                return;
            tcpListener.Stop();
            tcpListener = null;
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Listener for incoming connections stopped");
            OnStoppedListener?.Invoke();
        }


        private async void WaitConnectionOn(PortForClient port) {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port.portno);
            port.isUsed = true;
            TcpListener listenerOnSpecificPort = new TcpListener(endPoint);

            listenerOnSpecificPort.Start();

            //var asyncWait
            port.tcpClient = await listenerOnSpecificPort.AcceptTcpClientAsync();
            port.networkStream = port.tcpClient.GetStream();
            // client is now connected
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, $"Client is now connected on port {port.portno}");

            listenerOnSpecificPort.Stop();
            ListenToTcpClient(port, endPoint);

            OnClientConnected?.Invoke(port.portno);
        }

        private async void ListenToTcpClient(PortForClient port, IPEndPoint clientEndPoint) {
            byte[] buffer = new byte[512];
            int bytesRead;
            string msgReceivedSoFar = "";
            string finalizedMessage = "";
            bool repeatParsingFinalizedMsg = true;

            while (port.isUsed) {
                Array.Clear(buffer, 0, buffer.Length);
                try {
                    var waitMsgAsync = port.networkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (await Task.WhenAny(waitMsgAsync, Task.Delay(timeoutWithoutClientResponse)) != waitMsgAsync) {
                        OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, $"Timeout from port {port}");
                        DisconnectPort(port);
                        break;
                    }
                    bytesRead = await waitMsgAsync;
                } catch (Exception) {
                    DisconnectPort(port);
                    break;
                }

                if (!MyNetworkUtilities.IsBufferValid(buffer)) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Invalid buffer from client (client disconnected)");
                    DisconnectPort(port);
                    continue;
                }

                msgReceivedSoFar += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                repeatParsingFinalizedMsg = true;
                while (repeatParsingFinalizedMsg) {

                    (finalizedMessage, repeatParsingFinalizedMsg) = MyNetworkUtilities.ParseSplitMessage(ref msgReceivedSoFar,
                        MyNetworkUtilities.START_MESSAGE_CHAR, MyNetworkUtilities.END_MESSAGE_CHAR);

                    if (repeatParsingFinalizedMsg) {
                        InterpretFinalizedMessage(port, clientEndPoint, finalizedMessage);
                    }
                }

            }
        }

        private void InterpretFinalizedMessage(PortForClient port, IPEndPoint clientEndPoint, string finalMessage) {
            byte[] buffer = new byte[512];
            string msgReceived = "";
            MyNetworkUtilities.TcpMessageType typeReceived;
            (typeReceived, msgReceived) = MyNetworkUtilities.ExtractMessage(finalMessage);

            switch (typeReceived) {
                case MyNetworkUtilities.TcpMessageType.Ping:
                    // ping client back after i receive a ping
                    buffer = MyNetworkUtilities.ComposeMessageBytes(
                        MyNetworkUtilities.TcpMessageType.Ping, "Ping");
                    port.networkStream.Write(buffer, 0, buffer.Length);
                    break;
                case MyNetworkUtilities.TcpMessageType.Disconnect:
                    DisconnectPort(port);
                    break;
            }
            OnTCPClientReceivedMessage?.Invoke(clientEndPoint, port.portno, typeReceived, msgReceived);
        }

        private void DisconnectPort(PortForClient port) {
            if (!port.isUsed)
                return;

            // send msg to port that it's going to be disconnected
            byte[] buffer = MyNetworkUtilities.ComposeMessageBytes(MyNetworkUtilities.TcpMessageType.Disconnect,
                "disconnect");
            try {
                port.networkStream.Write(buffer, 0, buffer.Length);
            } catch (Exception) { }

            port.isUsed = false;
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, $"Port {port} disconnected.");
            port.networkStream.Close();
            port.tcpClient.Close();
            OnClientDisonnected?.Invoke(port.portno);

            if (autoStartListening && !ListeningForNewConnections && NrOfPortsAvaliable > 0) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Server listener auto started.");
                StartListeningForConnections();
            }
        }

        /// <summary>
        /// Closes all connections and stops the server from running
        /// </summary>
        public void StopServer() {
            serverOn = false;
            StopListening();
            udpReceiver.Close();
            foreach (PortForClient port in Ports) {
                DisconnectPort(port);
            }
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, $"Server stopped.");
            OnStoppedServer?.Invoke();
        }

        /// <summary>
        /// Broadcast a TCP message to all connected clients
        /// </summary>
        public void BroadcastMessage(MyNetworkUtilities.TcpMessageType messageType, string msg) {
            byte[] buffer;
            foreach (PortForClient port in Ports) {
                if (!port.isUsed)
                    continue;

                buffer = MyNetworkUtilities.ComposeMessageBytes(messageType, msg);
                port.networkStream.Write(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Send a TCP message on a specific port
        /// </summary>
        public void SendMessage(int portno, MyNetworkUtilities.TcpMessageType messageType, string msg) {
            foreach (PortForClient port in Ports) {
                if (port.portno != portno)
                    continue;
                byte[] buffer = MyNetworkUtilities.ComposeMessageBytes(messageType, msg);
                port.networkStream.Write(buffer, 0, buffer.Length);
                return;
            }
        }

        private async void ReceiveUdpMessages() {
            UdpReceiveResult udpResult;
            while (serverOn) {
                try {
                    udpResult = await udpReceiver.ReceiveAsync();
                } catch (SocketException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                        $"UDP socket is closed.");
                    return;
                } catch (ObjectDisposedException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                        $"UDP disposed.");
                    return;
                }

                string msgRecv = Encoding.ASCII.GetString(udpResult.Buffer, 0, udpResult.Buffer.Length);

                OnUDPReceivedMessage?.Invoke(udpResult.RemoteEndPoint, msgRecv);
            }
        }
    }
}
