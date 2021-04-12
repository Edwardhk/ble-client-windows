using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace EmpaticaBLEClient
{
    public class DataStream
    {
        public bool IfSubscribe = false;
        // Tuple<timestamp, <data1, data2...>>
        public List<Tuple<double, List<double>>> Data = new List<Tuple<double, List<double>>>();
        public List<string> StrData = new List<string>();

        public void PrintData()
        {
            foreach (var eachData in Data)
            {
                double timestamp = eachData.Item1;
                List<double> data = eachData.Item2;
                Console.Write("[T: " + timestamp + "]: ");
                foreach (double datapoint in data)
                {
                    Console.Write(datapoint + " ");
                }
                Console.WriteLine();
            }
        }
    }
    public class E4Device
    {
        const bool DEBUG_MODE = false;

        // Networking params
        const string ServerAddress = "127.0.0.1";
        const int ServerPort = 28000;
        public Socket TCPsocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Connection checking params
        private bool isConnectedBTLE = false;
        private bool isSubscribled = false;
        public bool isConnected = false;

        // Device params
        private const int REFRESH_INTERVAL = 5000;
        private const int RECORD_INTERVAL = 5000;
        private double latestBatteryLevel = 1.0;
        
        public string ID = "";
        public string DisplayName = "";
        public bool IfAllowed = false;
        public List<string> typesOfData = new List<string> {
            "E4_Acc", "E4_Bvp", "E4_Gsr", "E4_Temperature", "E4_Ibi", "E4_Hr", "E4_Battery", "E4_Tag"
        };
        public Dictionary<string, DataStream> dataStreams = new Dictionary<string, DataStream>();

        // ManualResetEvent instances signal completion and buffer
        private readonly ManualResetEvent ConnectDone = new ManualResetEvent(false);
        private readonly ManualResetEvent SendDone = new ManualResetEvent(false);
        private readonly ManualResetEvent ReceiveDone = new ManualResetEvent(false);
        string _response = "";

        public E4Device(string id, string displayName, bool ifAllowed)
        {
            ID = id;
            DisplayName = displayName;
            IfAllowed = ifAllowed;
            foreach (string type in typesOfData)
                dataStreams.Add(type, new DataStream());
        }

        public void StartE4Conn()
        {
            if (IfAllowed)
            {
                // Establish TCP socket for the device
                var ipHostInfo = new IPHostEntry { AddressList = new[] { IPAddress.Parse(ServerAddress) } };
                var ipAddress = ipHostInfo.AddressList[0];
                var remoteEp = new IPEndPoint(ipAddress, ServerPort);
                TCPsocket.BeginConnect(remoteEp, ConnectCallback, TCPsocket);
                ConnectDone.WaitOne();

                System.Timers.Timer timer = new System.Timers.Timer(1000);
                timer.Elapsed += (s, e) => ScheduledChecking();
                timer.Start();
            }
        }

        void ScheduledChecking()
        {
            if (!isConnected)
                CheckConnectionStatus();
            else
            {
                CheckBatteryStatus();
                CheckAndWriteToFile();
            }
            Receive();
            ReceiveDone.WaitOne();
            ReceiveDone.Reset();
        }

        void CheckBatteryStatus()
        {
            //Send("device_subscribe bat ON");
            //SendDone.WaitOne();
            //SendDone.Reset();
        }

        void CheckConnectionStatus()
        {
            // Establish listening callback
            Send("device_connect " + ID);
            SendDone.WaitOne();
            SendDone.Reset();
        }

        void CheckAndWriteToFile()
        {
            // File format: <YEAR>_<MONTH>_<DAY>_<TIME>
            foreach (string dataType in typesOfData)
            {
                var targetStrData = dataStreams[dataType].StrData;
                if (targetStrData.Count >= RECORD_INTERVAL)
                {
                    string dateAndTime = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
                    string filename = DisplayName + dataType + "_" + dateAndTime + ".csv";
                    using (StreamWriter sWriter = new StreamWriter(filename))
                    {
                        foreach(string dataPoint in targetStrData)
                            sWriter.Write(dataPoint.Replace(" ", ","));
                    }
                    targetStrData.Clear();
                }
            }

            //foreach(string dataLine in dataStreams[type].StrData)
            //{

            //}

            if (DEBUG_MODE)
                Console.WriteLine("[_IO_] Recorded, clearing data");
        }

        void ParseResponse(string res)
        {
            // It starts with either response(R) or data (E4_xxx)
            if (res[0] == 'R')
            {
                if (res.Contains("device_connect_btle OK"))
                {
                    isConnectedBTLE = true;
                    //// Bad hardcoded temp solution
                    //if (!isConnected)
                    //{
                    //    Send("device_connect " + ID);
                    //    SendDone.WaitOne();
                    //    Receive();
                    //    ReceiveDone.WaitOne();
                    //}
                }
                else if (res.Contains("device_connect OK"))
                {
                    // "E4_Gsr", "E4_Temp", "E4_Ibi", "E4_Hr"
                    isConnected = true;

                    string[] dataTypes = { "acc", "bvp", "gsr", "tmp", "ibi" };
                    foreach (string dataType in dataTypes)
                    {
                        Send(String.Format("device_subscribe {0} ON", dataType));
                        SendDone.WaitOne();
                        SendDone.Reset();
                        Thread.Sleep(100);
                    }
                }
                else if (res.Contains("device_subscribe acc OK"))
                {
                    isSubscribled = true;
                }
                else if (res.Contains("ERR The device requested for connection is not available"))
                {
                    isConnected = false;
                    isSubscribled = false;
                }
                else if(res.Contains("ERR You've tried to connect to the device from the same connection"))
                {
                    // Cannot validate if connected or not
                }
                else if(res.Contains("R connection lost to device"))
                {
                    isConnected = false;
                }
            }
            // Handle E4 data response (could be multi-line in single response)
            else if (res[0] == 'E')
            {
                if (DEBUG_MODE)
                    Console.WriteLine(res);
                string[] splitRes = res.Split('\n');

                foreach (string resLine in splitRes)
                {
                    // Handle empty line
                    if (resLine.Length == 0) continue;

                    // E4_Acc <TIMESTAMP> <> <> <>
                    // Others: E4_xxx <TIMESTAMP> <> <>
                    int dataLen = (resLine.Contains("E4_Acc ")) ? 3 : 2;
                    dataStreams[resLine.Split(' ')[0]].StrData.Add(resLine);
                    //Console.WriteLine(ID + ": [" + resLine.Split(' ')[0] + "]: " + resLine);
                }
            }
        }
        void Receive()
        {
            try
            {
                // Create the state object.
                var state = new StateObject { WorkSocket = TCPsocket };

                // Begin receiving the data from the remote device.
                TCPsocket.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        void Send(string cmd)
        {
            if (DEBUG_MODE)
                Console.WriteLine("[ <= ] Sent: " + cmd);

            // Establish connect request on TCP socket
            byte[] byteData = Encoding.ASCII.GetBytes(cmd + Environment.NewLine);
            TCPsocket.BeginSend(byteData, 0, byteData.Length, 0, SendCallback, TCPsocket);
        }
        void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                var client = (Socket)ar.AsyncState;
                client.EndConnect(ar);

                if (DEBUG_MODE)
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
                state.Sb.Clear();

                //if(DEBUG_MODE)
                //    Console.WriteLine("[ => ] Completed: " + _response);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
    public static class AsynchronousClient
    {
        //// The port number for the remote device.
        //private const string ServerAddress = "127.0.0.1";
        //private const int ServerPort = 28000;

        // Maintain the device connections
        private const int REFRESH_INTERVAL = 5000;
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
                var DevicesIDs = new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("HARDWARE ID ", "DISPLAY NAME")
                };

                foreach (var DeviceIDName in DevicesIDs)
                {
                    string DeviceID = DeviceIDName.Item1;
                    string DisplayName = DeviceIDName.Item2;
                    DeviceList.Add(DeviceID, new E4Device(DeviceID, DisplayName, true));
                    // Start non-blocking threads for multiple TCP conn
                    System.Timers.Timer timer = new System.Timers.Timer(500);
                    timer.Elapsed += (s, e) => {
                        DeviceList[DeviceID].StartE4Conn();
                    };
                    timer.AutoReset = false;
                    timer.Start();
                }

                while (true) {
                    Console.Clear();
                    // Keep main thread alive
                    foreach (var DeviceIDName in DevicesIDs)
                    {
                        string DeviceID = DeviceIDName.Item1;
                        string DisplayName = DeviceIDName.Item2;

                        string msg = "";

                        if (DeviceList[DeviceID].isConnected)
                        {
                            msg = "Connected with data size ---- ";

                            // "E4_Acc", "E4_Bvp", "E4_Gsr", "E4_Temperature", "E4_Ibi", "E4_Hr", "E4_Battery", "E4_Tag"
                            foreach (string dataType in DeviceList[DeviceID].typesOfData)
                            {
                                msg += dataType + ":" + DeviceList[DeviceID].dataStreams[dataType].StrData.Count + " ";
                            }
                        }
                        else
                            msg = "No Connection... Trying to reconnect...";
                        Console.WriteLine(String.Format("Device {0} status: {1}\r", DisplayName, msg));
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}