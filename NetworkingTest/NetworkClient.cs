using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Razziel.Network.Utilities;

namespace Razziel.Network {
    class NetworkClient {
        /// <summary>
        /// Used to connect with the listener
        /// </summary>
        private int retryTimesToConnectToListener = 50;

        /// <summary>
        /// Check if client is connected every x seconds
        /// </summary>
        private int pingDelaySeconds = 5;

        /// <summary>
        /// How many miliseconds should we wait for any response from server to consider it offline?
        /// </summary>
        private int timeoutWithoutServerResponse = 10000; // miliseconds


        private string ipAddress;
        /// <summary>
        /// Client on which the connection is established
        /// </summary>
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private UdpClient udpClient;

        private bool tryingToConnect = false;

        /// <summary>
        /// Is Client connected? This value is updated automatically
        /// </summary>
        public bool ClientConnected { get; private set; } = false;

        /// <summary>
        /// Event called when the client succesfully established the connection to server.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnClientConnected;
        /// <summary>
        /// Event called when the client failed to establish connection to server.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnClientFailedConnection;
        /// <summary>
        /// Event called when the client starts trying to connect to the server.
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnClientStartToConnect;
        /// <summary>
        /// Event called when the client was disconnected from server
        /// </summary>
        public event MyNetworkUtilities.ConnectionEvent OnClientDisconnected;
        /// <summary>
        /// Event called on receiving any type of messages from server.
        /// </summary>
        public event MyNetworkUtilities.ClientRecvMessageEvent OnReceivedTCPMessageFromServer;
        /// <summary>
        /// Event called when printing status of the connection.
        /// </summary>
        public event MyNetworkUtilities.ErrorLog OnLogStatus;

        /// <summary>
        /// Constructor of a client
        /// </summary>
        /// <param name="retryTimesToConnect">How many times should the client try to connect to the
        /// server listener?</param>
        /// <param name="pingDelaySeconds">What's the delay in seconds to ping the server?
        /// Every time a ping is received by the server, it will send back to the client.</param>
        /// <param name="timeoutWihtoutResponseSeconds">What's the maximum number of seconds admited
        /// to not receive any message from the server before client considers it a timeout.
        /// Must be bigger than pingDelaySeconds</param>
        public NetworkClient(int retryTimesToConnect = 50,
            int pingDelaySeconds = 10, int timeoutWihtoutResponseSeconds = 15) {
            if (timeoutWihtoutResponseSeconds < pingDelaySeconds) {
                timeoutWihtoutResponseSeconds = pingDelaySeconds + 1;
            }

            this.pingDelaySeconds = pingDelaySeconds;
            this.timeoutWithoutServerResponse = timeoutWihtoutResponseSeconds * 1000;
            this.retryTimesToConnectToListener = retryTimesToConnect;
        }

        /// <summary>
        /// Starts client and connects it to the given address. This is a non-blocking function.
        /// </summary>
        /// <param name="ipAddress">Connect to this ip address</param>
        /// <param name="portListenedByServer">The port to connect connect to.
        /// Same port where the server listens</param>
        /// <returns>True if connection was successful. Result is also stored in ClientConnected</returns>
        public async Task<bool> ConnectClient(string ipAddress, int portListenedByServer) {
            if (ClientConnected) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Client already connected. Connection aborted");
                return false;
            }
            if (tryingToConnect) {
                return false;
            }
            tryingToConnect = true;
            OnClientStartToConnect?.Invoke();

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), portListenedByServer);
            TcpClient tcpPingClient = new TcpClient();
            bool connected = false;

            ClientConnected = false;
            this.ipAddress = ipAddress;

            // try to connect
            for (int i = 0; i < retryTimesToConnectToListener; i++) {
                try {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                        "Client trying to connect to listener . . .");
                    await tcpPingClient.ConnectAsync(ipAddress, portListenedByServer);
                    connected = true;
                    break;
                } catch (SocketException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Error,
                        "Listening socket error.");
                } catch (ObjectDisposedException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Error,
                        "Tcp to listener disposed");
                    return false;
                }
            }
            if (!connected) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                    "Client could not connect to the server listener.");
                return false;
            }
            // is now connected
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                "Client connected to listener.");

            // get stream of communication
            NetworkStream ns = tcpPingClient.GetStream();
            byte[] buffer = new byte[512];
            // receive message where to connect now.
            int bytesRead = ns.Read(buffer, 0, buffer.Length);
            // the message is given like this "xxx:tcpPort:udpPort". we can now connect to that port
            string[] splitReadString = Encoding.ASCII.GetString(buffer, 0, buffer.Length).Split(':');
            int connectToPort = int.Parse(splitReadString[1]);
            int udpPort = int.Parse(splitReadString[2]);


            ns.Close();
            tcpPingClient.Close();

            bool connectedToPort = ConnectToPort(connectToPort);

            if (connectedToPort) {
                StartAutoSendingPings();
                ReceiveTCPMessages();


                udpClient = new UdpClient();
                udpClient.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), udpPort));

                OnClientConnected?.Invoke();
                tryingToConnect = false;
                return true;
            } else {
                OnClientFailedConnection?.Invoke();
                tryingToConnect = false;
                return false;
            }
        }

        private bool ConnectToPort(int port) {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
            tcpClient = new TcpClient();
            try {
                tcpClient.Connect(endPoint);
            } catch (SocketException) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Error,
                    "Socket disabled for the given port from the listener.");
                return false;
            } catch (ObjectDisposedException) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Error,
                    "Tcp client disposed when connecting to the given port by listener");
                return false;
            }
            // client is now connected
            networkStream = tcpClient.GetStream();
            ClientConnected = true;

            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                $"Client connected to {ipAddress}:{port}.");
            return true;
        }

        public void Disconnect() {
            ClientConnected = false;
            if (tcpClient == null) {
                return; // already disconnected
            }

            // send msg to server that i'm going to disconnect
            byte[] buffer = MyNetworkUtilities.ComposeMessageBytes(MyNetworkUtilities.TcpMessageType.Disconnect,
                "disconnect");
            try {
                networkStream.Write(buffer, 0, buffer.Length);
            } catch (Exception) { }

            networkStream.Close();
            networkStream.Dispose();
            tcpClient.Close();
            tcpClient.Dispose();
            tcpClient = null;
            OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log,
                "Client disconnected.");

            OnClientDisconnected?.Invoke();
        }

        /// <summary>
        /// Starts to send a ping message to server every x seconds.
        /// </summary>
        private async void StartAutoSendingPings() {
            byte[] buffer = new byte[256];

            await Task.Delay(TimeSpan.FromSeconds(pingDelaySeconds));
            while (ClientConnected) {
                OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Sent ping.");

                buffer = MyNetworkUtilities.ComposeMessageBytes(
                    MyNetworkUtilities.TcpMessageType.Ping, "Ping");
                networkStream.Write(buffer, 0, buffer.Length);
                await Task.Delay(TimeSpan.FromSeconds(pingDelaySeconds));
            }
        }

        /// <summary>
        /// Receive and interpret any messages coming from server
        /// </summary>
        private async void ReceiveTCPMessages() {
            byte[] buffer = new byte[512];
            int bytesRead;
            string msgReceivedSoFar = "";
            string finalizedMessage = "";
            bool repeatParsingFinalizedMsg = true;

            while (ClientConnected) {
                try {
                    var waitMsgAsync = networkStream.ReadAsync(buffer, 0, buffer.Length);

                    if (await Task.WhenAny(waitMsgAsync, Task.Delay(timeoutWithoutServerResponse)) != waitMsgAsync) {
                        OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Read socket timeout.");
                        Disconnect();
                        return;
                    }
                    bytesRead = await waitMsgAsync;
                } catch (System.IO.IOException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Read socket closed.");
                    Disconnect();
                    return;
                } catch (ObjectDisposedException) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Read socket disposed.");
                    Disconnect();
                    return;
                }

                if (!MyNetworkUtilities.IsBufferValid(buffer)) {
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Invalid buffer from server (disconnected by server)");
                    Disconnect();
                    continue;
                }


                msgReceivedSoFar += Encoding.ASCII.GetString(buffer, 0, bytesRead);

                repeatParsingFinalizedMsg = true;
                while (repeatParsingFinalizedMsg) {

                    (finalizedMessage, repeatParsingFinalizedMsg) = MyNetworkUtilities.ParseSplitMessage(ref msgReceivedSoFar,
                        MyNetworkUtilities.START_MESSAGE_CHAR, MyNetworkUtilities.END_MESSAGE_CHAR);

                    if (repeatParsingFinalizedMsg) {
                        InterpretFinalizedMessage(finalizedMessage);
                    }
                }
            }
        }

        private void InterpretFinalizedMessage(string finalizedMessage) {
            string msgReceived = "";
            MyNetworkUtilities.TcpMessageType typeReceived;
            (typeReceived, msgReceived) = MyNetworkUtilities.ExtractMessage(finalizedMessage);

            switch (typeReceived) {
                case MyNetworkUtilities.TcpMessageType.Ping:
                    OnLogStatus?.Invoke(MyNetworkUtilities.LogType.Log, "Pinged by server because i pinged.");
                    break;
                case MyNetworkUtilities.TcpMessageType.Disconnect:
                    Disconnect();
                    break;
            }
            OnReceivedTCPMessageFromServer?.Invoke(typeReceived, msgReceived);

        }

        public void SendMessageTCP(MyNetworkUtilities.TcpMessageType messageType, string msg) {
            if (!ClientConnected)
                return;
            byte[] buffer = MyNetworkUtilities.ComposeMessageBytes(messageType, msg);
            networkStream.Write(buffer, 0, buffer.Length);
        }

        public void SendMessageUDP(string msg) {
            if (!ClientConnected)
                return;
            byte[] buffer = Encoding.ASCII.GetBytes(msg);
            udpClient.Send(buffer, buffer.Length);
        }
    }
}
