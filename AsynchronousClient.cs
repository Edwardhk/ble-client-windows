using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace EmpaticaBLEClient
{
    public class DataStream
    {
        public bool IfSubscribe = false;
        public List<Tuple<double, double>> Data = new List<Tuple<double, double>>();
    }
    public class E4Device
    {
        // Networking params
        const string ServerAddress = "127.0.0.1";
        const int ServerPort = 28000;
        public Socket TCPsocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        string _response = "";

        // Device params
        public string ID = "";
        public bool IfAllowed = false;
        public List<string> typesOfData = new List<string> {
            "acc", "bvp", "gsr", "ibi", "tmp", "bat", "tag"
        };
        public Dictionary<string, DataStream> dataStreams = new Dictionary<string, DataStream>();

        // ManualResetEvent instances signal completion.
        private readonly ManualResetEvent ConnectDone = new ManualResetEvent(false);
        private readonly ManualResetEvent SendDone = new ManualResetEvent(false);
        private readonly ManualResetEvent ReceiveDone = new ManualResetEvent(false);

        public E4Device(string id, bool ifAllowed)
        {
            ID = id;
            IfAllowed = ifAllowed;
            foreach (string type in typesOfData)
                dataStreams.Add(type, new DataStream());

            if (ifAllowed)
            {
                // Establish TCP socket for the device
                var ipHostInfo = new IPHostEntry { AddressList = new[] { IPAddress.Parse(ServerAddress) } };
                var ipAddress = ipHostInfo.AddressList[0];
                var remoteEp = new IPEndPoint(ipAddress, ServerPort);
                TCPsocket.BeginConnect(remoteEp, (ConnectCallback), TCPsocket);
                ConnectDone.WaitOne();

                // Establish listening callback

                SendCommandAndReceive("device_connect_btle " + ID);
                SendCommandAndReceive("device_subscribe gsr ON");
            }
            MainCycle();
        }

        void MainCycle()
        {
            while (true)
            {
                Thread.Sleep(1000);
                var state = new StateObject { WorkSocket = TCPsocket };
                TCPsocket.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
            }
        }

        void SendCommandAndReceive(string cmd)
        {
            Console.WriteLine("[ => ] Sent: " + cmd);

            // Establish connect request on TCP socket
            byte[] byteData = Encoding.ASCII.GetBytes(cmd + Environment.NewLine);
            TCPsocket.BeginSend(byteData, 0, byteData.Length, 0, SendCallback, TCPsocket);
            SendDone.WaitOne();

            var state = new StateObject { WorkSocket = TCPsocket };
            TCPsocket.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
            ReceiveDone.WaitOne();
        }

        void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                var client = (Socket)ar.AsyncState;
                client.EndConnect(ar);
                Console.WriteLine("[CONN] Established TCP socket for ID {0}", ID);
                ConnectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                var client = (Socket)ar.AsyncState;
                // Complete sending the data to the remote device.
                client.EndSend(ar);
                SendDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                var state = (StateObject)ar.AsyncState;
                var client = state.WorkSocket;

                // Read data from the remote device.
                var bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    state.Sb.Append(Encoding.ASCII.GetString(state.Buffer, 0, bytesRead));
                    _response = state.Sb.ToString();
                    //ParseResponse(_response);
                    state.Sb.Clear();

                    ReceiveDone.Set();

                    // Get the rest of the data.
                    client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    if (state.Sb.Length > 1)
                    {
                        _response = state.Sb.ToString();
                    }
                    // Signal that all bytes have been received.
                    //ReceiveDone.Set();
                }

                Console.WriteLine("[ => ] Completed: " + _response);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        void SubscribeToData()
        {

        }
    }
    public static class AsynchronousClient
    {
        // The port number for the remote device.
        private const string ServerAddress = "127.0.0.1";
        private const int ServerPort = 28000;

        // Maintain the device connections
        private const int REFRESH_INTERVAL = 1000;
        private static HashSet<string> DiscoveredDeviceID = new HashSet<string>();
        private static HashSet<string> ActiveDeviceID = new HashSet<string>();
        private static Dictionary<string, E4Device> DeviceList = new Dictionary<string, E4Device>();

        // ManualResetEvent instances signal completion.
        private static readonly ManualResetEvent ConnectDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent SendDone = new ManualResetEvent(false);
        private static readonly ManualResetEvent ReceiveDone = new ManualResetEvent(false);

        // The response from the remote device.
        private static String _response = String.Empty;

        public static void StartClient()
        {
            // Connect to a remote device.
            try
            {
                // Establish the remote endpoint for the socket.
                var ipHostInfo = new IPHostEntry { AddressList = new[] { IPAddress.Parse(ServerAddress) } };
                var ipAddress = ipHostInfo.AddressList[0];
                var remoteEp = new IPEndPoint(ipAddress, ServerPort);

                // Create a TCP/IP socket.
                var defaultClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Connect to the remote endpoint.
                defaultClient.BeginConnect(remoteEp, (ConnectCallback), defaultClient);
                ConnectDone.WaitOne();

                while (true)
                {
                    Send(defaultClient, "device_discover_list" + Environment.NewLine);
                    Receive(defaultClient);
                    Thread.Sleep(REFRESH_INTERVAL);
                    //Console.WriteLine("[DEBUG] DiscoveredDeviceID: " + DiscoveredDeviceID.Count);
                    //Console.WriteLine("[DEBUG] ActiveDeviceID: " + ActiveDeviceID.Count);
                    //Console.WriteLine("[DEBUG] DeviceList: " + DeviceList.Count);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ParseResponse(string response)
        {
            //Console.Write(response);
            const string DEVICE_LIST = "R device_list ";
            const string DEVICE_DISCOVER_LIST = "R device_discover_list ";

            if(response.Contains(DEVICE_LIST) || response.Contains(DEVICE_DISCOVER_LIST))
            {
                List<string> entities = response.Split('|').ToList();
                if (entities.Count == 1)
                    return;
                else
                {
                    entities.RemoveAt(0);
                    foreach (string entity in entities)
                    {
                        // zero padded
                        string device_id = entity.Split(' ')[1];
                        string device_model = entity.Split(' ')[2]; // By default it's Empatica_E4
                        bool isAllowed = !entity.Split(' ')[3].Contains("not_allowed");
                        if (!DiscoveredDeviceID.Contains(device_id))
                        {
                            // First time discovery
                            DiscoveredDeviceID.Add(device_id);
                            DeviceList.Add(device_id, new E4Device(device_id, isAllowed));
                            if (isAllowed) ActiveDeviceID.Add(device_id);
                        }
                    }
                }
            }
        }

        private static void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                var client = (Socket)ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);

                Console.WriteLine("Socket connected to {0}", client.RemoteEndPoint);

                // Signal that the connection has been made.
                ConnectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                var state = new StateObject { WorkSocket = client };

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                var state = (StateObject)ar.AsyncState;
                var client = state.WorkSocket;

                // Read data from the remote device.
                var bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    state.Sb.Append(Encoding.ASCII.GetString(state.Buffer, 0, bytesRead));
                    _response = state.Sb.ToString();

                    ParseResponse(_response);

                    state.Sb.Clear();

                    ReceiveDone.Set();

                    // Get the rest of the data.
                    client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    if (state.Sb.Length > 1)
                    {
                        _response = state.Sb.ToString();
                    }
                    // Signal that all bytes have been received.
                    ReceiveDone.Set();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void Send(Socket client, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            client.BeginSend(byteData, 0, byteData.Length, 0, SendCallback, client);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                var client = (Socket)ar.AsyncState;
                // Complete sending the data to the remote device.
                client.EndSend(ar);
                // Signal that all bytes have been sent.
                SendDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}