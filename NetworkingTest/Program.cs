using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;


namespace BogdanCodreanu.Network {
    class Program {
        private static int udpPort = 29970;
        private static int tcpPortListen = 29971;
        private static int tcpPortStart = 29972;

        static void Main(string[] args) {
            string input;

            GetLocalIps(true);
            Console.WriteLine();
            Console.WriteLine("1. Start as server\n2. Start as client\n3. Start server and client");

            if (args.Length > 0) {
                StartAsClient();
                return;
            }


            input = Console.ReadLine();

            if (input.Equals("1")) {
                StartAsServer();
            }
            if (input.Equals("2")) {
                StartAsClient();
            }
            if (input.Equals("3")) {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.FileName = "C:/Users/Razziel/source/repos/NetworkingTest/NetworkingTest/bin/Debug/" +
                    "NetworkingTest.exe";
                startInfo.Arguments = "1";
                System.Diagnostics.Process.Start(startInfo);
                StartAsServer();
            }

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        private static void StartAsServer() {
            NetworkServer server = new NetworkServer(20, autoStartListening: true);
            string ipAddressServer = GetLocalIps();
            server.StartServer(ipAddressServer, udpPort, tcpPortListen, tcpPortStart, 2);
            server.OnLogStatus += LogMessage;

            server.OnUDPReceivedMessage += delegate (IPEndPoint IPEndPoint, string message) {
                Console.WriteLine($"UDP from {IPEndPoint.Address.ToString()}: {message}");
            };
            server.OnTCPClientReceivedMessage += delegate (IPEndPoint IPEndPoint,
                int portno, Utilities.MyNetworkUtilities.TcpMessageType type, string message) {
                    Console.WriteLine($"TCP from {IPEndPoint.Address.ToString()}: {message}");
                };

            string inputString = null;
            while (!(inputString = Console.ReadLine()).ToUpper().Equals("EXIT")) {
                inputString = inputString.ToUpper();
                switch (inputString) {
                    case "STOP":
                        server.StopListening();
                        break;
                    case "START":
                        server.StartListeningForConnections();
                        break;
                    default:
                        server.BroadcastMessage(Utilities.MyNetworkUtilities.TcpMessageType.Message, inputString);
                        break;
                }
            }
            server.StopServer();
        }

        private static void StartAsClient() {
            NetworkClient client = new NetworkClient(10, 5, 7);
            string ipAddressServer = GetLocalIps();
            client.ConnectClient(ipAddressServer, tcpPortListen);
            client.OnLogStatus += LogMessage;

            client.OnReceivedTCPMessageFromServer +=
                delegate (Utilities.MyNetworkUtilities.TcpMessageType type, string message) {
                    Console.WriteLine("TCP from server: " + message);
                };

            string inputString = null;
            while (!(inputString = Console.ReadLine()).ToUpper().Equals("EXIT")) {
                client.SendMessageTCP(Utilities.MyNetworkUtilities.TcpMessageType.Message, inputString);
                client.SendMessageUDP(inputString);
            }
            client.Disconnect();
        }

        private static string GetLocalIps(bool printIps = false) {
            string firstIp = null;
            IPAddress[] localIps = Dns.GetHostAddresses(Dns.GetHostName());

            if (printIps) {
                Console.WriteLine($"Host name is {Dns.GetHostName()}");
            }

            foreach (IPAddress addr in localIps) {
                if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;

                if (printIps) {
                    Console.WriteLine($"A local ip is {addr}");
                }
                if (firstIp == null) {
                    firstIp = addr.ToString();
                }
            }
            return firstIp;
        }


        private static void LogMessage(Utilities.MyNetworkUtilities.LogType type, string message) {
            Console.WriteLine($"{type.ToString().ToUpper()}: {message}");
        }

    }
}
