# C# TCP UDP clients server
 A library of 2 classes that are easy to use, allowing quick conenction between 2 devices.
 
 How to use:
Import files NetworkClient.cs, NetworkServer.cs and NetworkUtilities.cs in your project.
Include namespace Razziel.Network.
NetworkServer functionalities:
  StartServer
  StartListeningForConnections
  StopListening
  StopServer
  BroadcastTCPMessage
  SendTcpMessage
  
  Including the following events: OnStartedServer, OnStoppedServer, OnStartedListener, OnStoppedListener, OnClientConnected,
  OnClientDisonnected, OnReceivedTCPMessage, OnReceivedUDPMessage

NetworkClient functionalities:
  ConnectClient
  Disconnect
  SendMessageTCP
  SendMessageUDP
  
  Including the following events: OnClientConnected, OnClientFailedConnection, OnClientStartToConnect, OnClientDisconnected, OnReceivedTCPMessageFromServer
