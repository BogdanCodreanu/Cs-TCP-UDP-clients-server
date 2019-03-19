using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Razziel.Network.Utilities {
    class MyNetworkUtilities {
        public const char START_MESSAGE_CHAR = '\u0002';
        public const char END_MESSAGE_CHAR = '\u0003';
        
        // if you desire to add more message types, then make sure you add them in the
        // following dictionaries.
        public enum TcpMessageType {
            /// <summary>
            /// Used by client and server automatically. Should not send these kind of messages.
            /// </summary>
            Ping,
            /// <summary>
            /// Used by client and server automatically. DO NOT send these kind of messages.
            /// </summary>
            Disconnect,
            /// <summary>
            /// Any desired message. Interpret it as you desire.
            /// </summary>
            Message,
        }
        // here
        private static Dictionary<TcpMessageType, int> TcpTypeToNumberDictionary =
            new Dictionary<TcpMessageType, int>() {
            { TcpMessageType.Ping, 0 },
            { TcpMessageType.Disconnect, 1 },
            { TcpMessageType.Message, 2 },
        };
        // and here
        private static Dictionary<int, TcpMessageType> NumberToTcpTypeDictionary = 
            new Dictionary<int, TcpMessageType>() {
            { 0, TcpMessageType.Ping },
            { 1, TcpMessageType.Disconnect },
            { 2, TcpMessageType.Message },
        };

        /// <summary>
        /// Composes a message string to be placed inside a buffer.
        /// </summary>
        private static string ComposeMessage(TcpMessageType type, string message) {
            return TcpTypeToNumberDictionary[type] + "|" + message + "|";
        }

        /// <summary>
        /// Composes a byte array ready to be send inside a networkStream
        /// </summary>
        public static byte[] ComposeMessageBytes(TcpMessageType type, string message) {
            string msg = START_MESSAGE_CHAR + ComposeMessage(type, message) + END_MESSAGE_CHAR;
            return Encoding.ASCII.GetBytes(msg.ToCharArray(), 0, msg.Length);
        }
         /// <summary>
         /// Extracts message details from a buffer that came from a networkStream
         /// </summary>
        public static (TcpMessageType type, string msg) ExtractMessage(byte[] buffer, int bytesRead) {
            string msg = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            
            Console.WriteLine($"Msg extracting '{msg}'");
            string[] split = msg.Split('|');
            return (NumberToTcpTypeDictionary[int.Parse(split[0])], split[1]);
        }

        /// <summary>
        /// Extract data from a finalized message given from network.
        /// </summary>
        /// <param name="messageFinale">A message that was between a startMessageChar and 
        /// endMessageChar</param>
        /// <returns></returns>
        public static (TcpMessageType type, string msg) ExtractMessage(string messageFinale) {
            string[] split = messageFinale.Split('|');
            return (NumberToTcpTypeDictionary[int.Parse(split[0])], split[1]);
        }

        public static bool IsBufferValid(byte[] buffer) {
            for (int i = 0; i < buffer.Length; i++) {
                if (buffer[i] != 0)
                    return true;
            }
            return false;
        }

        public enum LogType {
            Log,
            Error,
        }

        public delegate void ConnectionEvent();
        public delegate void ClientRecvMessageEvent(TcpMessageType type, string message);
        public delegate void ErrorLog(LogType type, string message);

        public delegate void ServerConnectionEvent(int portno);
        public delegate void ServerRecvTCPMessageEvent(IPEndPoint IPEndPoint, int portno, TcpMessageType type, string message);
        public delegate void ServerRecvUDPMessageEvent(IPEndPoint IPEndPoint, string message);

        /// <summary>
        /// Searches into a message and extracts a substring contained inside the given message,
        /// between the given characters. Also removes the extracted message from the string, 
        /// deleting everything before the found extracted word. Including the word istelf
        /// </summary>
        /// <param name="messageSoFar">The message that can contain the given char separators.
        /// Will also be substring-ed after a the 2 given chars are found in the message.
        /// Everything before the first endMsgChar found will be removed.</param>
        /// <param name="startMsgChar">Start of the message you want to extract</param>
        /// <param name="endMsgChar">End of the message you want to extract</param>
        /// <returns>The extracted message (can be null if not found) and
        /// whether or not the extracted message has been found. If found, you
        /// should call this again, in order to check for another possible existing extraction
        /// after the messageSoFar has been substring-ed.</returns>
        public static (string finalizedMessage, bool foundAndShouldRepeat) ParseSplitMessage
            (ref string messageSoFar, char startMsgChar, char endMsgChar) {


            // find first end char.
            int indexOfEndCharMsg = messageSoFar.IndexOf(endMsgChar);
            if (indexOfEndCharMsg == -1)
                return (null, false);

            int indexOfStartCharMsg;

            //for (indexOfStartCharMsg = indexOfEndCharMsg - 1; 
            //    indexOfStartCharMsg >= 0; indexOfStartCharMsg--) {

            //    if (messageSoFar[indexOfStartCharMsg] == startMsgChar) {
            //        break;
            //    }
            //}

            // find first start char
            indexOfStartCharMsg = messageSoFar.IndexOf(startMsgChar);
            if (indexOfStartCharMsg == -1)
                return (null, false);


            // message is between the 2 characters
            string finalizedMessage = messageSoFar.Substring(indexOfStartCharMsg + 1,
                indexOfEndCharMsg - indexOfStartCharMsg - 1);
            
            // and we delete from msg so far the finalized message.
            messageSoFar = messageSoFar.Substring(indexOfEndCharMsg + 1);

            return (finalizedMessage, true);
            
        }
    }
}
