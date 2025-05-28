using Silkroad.Network;
using Silkroad.SecurityAPI;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace SimplestSilkroadFilter
{
    class Program
    {
        #region Private Members
        private static string mGatewayAddress;
        private static ushort mGatewayPort;

        private static string mPublicHost;
        private static ushort mBindGatewayPort, mBindAgentPort, mBindDownloadPort;

        private static Dictionary<string, List<object[]>> mAgentServerQueue = new Dictionary<string, List<object[]>>();
        private static TimeSpan mAgentServerQueueTimeLimit = new TimeSpan(0,0,5);
        private static Dictionary<string, List<object[]>> mDownloadServerQueue = new Dictionary<string, List<object[]>>();
        private static TimeSpan mDownloadServerQueueTimeLimit = new TimeSpan(0, 0, 5);
        #endregion

        #region Entry Point
        static void Main(string[] args)
        {
            // Set console
            Console.Title = "Simplest Silkroad Filter - https://github.com/JellyBitz/SimplestSilkroadFilter";
            Console.WriteLine(Console.Title + Environment.NewLine);

            // Command line settings
            LoadCommandLine(args);

            // Make sure the ip is working
            if (mGatewayAddress == null)
            {
                Console.WriteLine("Error: Gateway Host or IP not found or cannot be resolved." + Environment.NewLine);
                DisplayUsage();
                DisplayPause();
                return;
            }
            if(mGatewayPort == 0)
            {
                Console.WriteLine("Error: Gateway Port not being set." + Environment.NewLine);
                DisplayUsage();
                DisplayPause();
                return;
            }
            var localIPAddresses = GetMyLocalAddresses();
            if (mPublicHost == null)
                mPublicHost = GetAllMyAddresses().Last();
            else
                Console.WriteLine("Using as public host [" + mPublicHost + "]");

            #region Download Server Setup
            // Initialize
            AsyncServer dlServer = new AsyncServer();

            // Log connections
            dlServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Download Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            dlServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Download Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            // Download server control
            dlServer.OnProxyConnection += (_s, _e) =>
            {
                var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();
                var isLocal = localIPAddresses.Find(x => x == clientIP);
                if (isLocal != null)
                    clientIP = "localhost";

                // Check connections controller
                if (mDownloadServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                {
                    // Check all connections from this IP and remove the old ones
                    object[] clientQueue = null;
                    for (int i = 0; i < connections.Count; i++)
                    {
                        Stopwatch connectionTime = (Stopwatch)connections[i][2];
                        // Remove connections longer than one minute
                        if (connectionTime.Elapsed > mDownloadServerQueueTimeLimit)
                        {
                            connections.RemoveAt(i--);
                            continue;
                        }
                        else
                        {
                            clientQueue = connections[i];
                            connections.RemoveAt(i);
                            break;
                        }
                    }

                    // Set proxy connection
                    if (clientQueue == null)
                    {
                        // Shutdown connection if cannot be found
                        _e.Proxy.Server.Close();
                    }
                    else
                    {
                        // Redirect connection
                        _e.IP = (string)clientQueue[0];
                        _e.Port = (ushort)clientQueue[1];
                    }
                }
            };

            // Start
            Console.WriteLine("Download Server: Initializing...");
            try
            {
                dlServer.Start(mBindDownloadPort);
                mBindDownloadPort = (ushort)dlServer.Port;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                DisplayPause();
                return;
            }
            Console.WriteLine("Download Server: Started successfully on port [" + dlServer.Port + "]");
            #endregion

            #region Agent Server Setup
            // Initialize
            AsyncServer agServer = new AsyncServer();

            // Agent server queue control - WHY? To avoid IP change (kind of exploit) at login
            agServer.OnProxyConnection += (_s, _e) =>
            {
                var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();
                var isLocal = localIPAddresses.Find(x => x == clientIP);
                if (isLocal != null)
                    clientIP = "localhost";

                // Check connections controller
                if (mAgentServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                {
                    // Check all connections from this IP and remove the old ones
                    object[] clientQueue = null;
                    for (int i = 0; i < connections.Count; i++)
                    {
                        Stopwatch connectionTime = (Stopwatch)connections[i][2];
                        // Remove connections longer than one minute
                        if (connectionTime.Elapsed > mAgentServerQueueTimeLimit)
                        {
                            connections.RemoveAt(i--);
                            continue;
                        }
                        else
                        {
                            clientQueue = connections[i];
                            connections.RemoveAt(i);
                            break;
                        }
                    }

                    // Set proxy connection
                    if (clientQueue == null)
                    {
                        // Shutdown connection if cannot be found
                        _e.Proxy.Server.Close();
                    }
                    else
                    {
                        // Redirect connection
                        _e.IP = (string)clientQueue[0];
                        _e.Port = (ushort)clientQueue[1];
                    }
                }
            };

            // Log connections
            agServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Agent Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            agServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Agent Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };

            // Start
            Console.WriteLine("Agent Server: Initializing...");
            try
            {
                agServer.Start(mBindAgentPort);
                mBindAgentPort = (ushort)agServer.Port;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                DisplayPause();
                return;
            }
            Console.WriteLine("Agent Server: Started successfully on port [" + agServer.Port + "]");
            #endregion

            #region Gateway Server Setup
            // Initialize
            AsyncServer gwServer = new AsyncServer();
            gwServer.SetProxy(mGatewayAddress, mGatewayPort);
            // Log connections
            gwServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            gwServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            // Patch Response - Redirecting IP from Silkroad into this Proxy
            gwServer.RegisterServerPacketHandler(0xA100, (_s, _e) =>
            {
                var packet = _e.Packet;
                // Patch error
                if (packet.ReadByte() == 2)
                {
                    // Downloading patch required
                    if (packet.ReadByte() == 2)
                    {
                        var downloadServerIP = packet.ReadAscii();
                        var downloadServerPort = packet.ReadUShort();

                        var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();
                        // Check if client connection is from an external address to redirect them properly
                        var isLocal = localIPAddresses.Find(x => x == clientIP);
                        if (isLocal != null)
                            clientIP = "localhost";
                        else
                            downloadServerIP = mPublicHost;

                        // Add this connection to the agent server queue control
                        if (!mDownloadServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                        {
                            connections = new List<object[]>();
                            mDownloadServerQueue[clientIP] = connections;
                        }
                        connections.Add(new object[]
                        {
                        downloadServerIP, // Server IP
                        downloadServerPort, // Server Port
                        Stopwatch.StartNew(), // Time register to discard connection after some time
                        });

                        Console.WriteLine("Gateway Server: Redirecting connection from (" + _e.Proxy.Client.Socket.LocalEndPoint + ") to local Download Server");
                        // Redirect client to the local download server
                        var p = new Packet(0xA100, packet.Encrypted, packet.Massive);
                        p.WriteByte(2);
                        p.WriteByte(2);
                        p.WriteAscii(downloadServerIP);
                        p.WriteUShort(mBindDownloadPort);
                        p.WriteByteArray(packet.ReadByteArray(packet.RemainingRead())); // Copy data left
                        _e.Proxy.Client.Send(p);

                        // Avoid send this packet already handled to client
                        _e.CancelTransfer = true;

                    }
                }

            });
            // Login Response - Redirecting IP from Silkroad into this Proxy
            gwServer.RegisterServerPacketHandler(0xA102, (_s, _e) =>
            {
                var packet = _e.Packet;
                // Check success
                if (packet.ReadByte() == 1)
                {
                    var queueId = packet.ReadUInt();
                    var agentServerIP = packet.ReadAscii();
                    var agentServerPort = packet.ReadUShort();

                    var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();
                    // Check if client connection is from an external address to redirect them properly
                    var isLocal = localIPAddresses.Find(x => x == clientIP);
                    if (isLocal != null)
                        clientIP = "localhost";
                    else
                        agentServerIP = mPublicHost;

                    // Add this connection to the agent server queue control
                    if (!mAgentServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                    {
                        connections = new List<object[]>();
                        mAgentServerQueue[clientIP] = connections;
                    }
                    connections.Add(new object[]
                    {
                        agentServerIP, // Server IP
                        agentServerPort, // Server Port
                        Stopwatch.StartNew(), // Time register to discard connection after some time
                    });

                    Console.WriteLine("Gateway Server: Redirecting connection from (" + _e.Proxy.Client.Socket.LocalEndPoint + ") to local Agent Server");
                    // Redirect client to the local agent server
                    var p = new Packet(0xA102, packet.Encrypted, packet.Massive);
                    p.WriteByte(1); // success
                    p.WriteUInt(queueId);
                    p.WriteAscii(agentServerIP);
                    p.WriteUShort(mBindAgentPort);
                    _e.Proxy.Client.Send(p);

                    // Avoid send this packet already handled to client
                    _e.CancelTransfer = true;
                }
            });

            // Start
            Console.WriteLine("Gateway Server: Initializing...");
            try
            {
                gwServer.Start(mBindGatewayPort);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                DisplayPause();
                return;
            }
            Console.WriteLine("Gateway Server: Started successfully on port [" + gwServer.Port + "]");
            #endregion

            // Reading ENTER to exit
            Console.WriteLine(Environment.NewLine + "Press ESCAPE anytime to exit . . .");
            while (Console.ReadKey(false).Key != ConsoleKey.Escape);
        }
        #endregion

        #region Private Helpers
        /// <summary>
        /// Load defined command line
        /// </summary>
        static void LoadCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                // The command will not be sensitive
                string cmd = args[i].ToLower();

                // Check commands with data
                if (cmd.StartsWith("-bind-gw-port="))
                {
                    if(ushort.TryParse(cmd.Substring("-bind-gw-port=".Length), out var value))
                        mBindGatewayPort = value;
                }
                else if (cmd.StartsWith("-bind-ag-port="))
                {
                    if (ushort.TryParse(cmd.Substring("-bind-ag-port=".Length), out var value))
                        mBindAgentPort = value;
                }
                else if (cmd.StartsWith("-bind-dl-port="))
                {
                    if (ushort.TryParse(cmd.Substring("-bind-dl-port=".Length), out var value))
                        mBindDownloadPort = value;
                }
                else if (cmd.StartsWith("-gw-host="))
                {
                    var value = cmd.Substring("-gw-host=".Length);
                    if (!string.IsNullOrEmpty(value) && GetIPAddress(value) != null)
                        mGatewayAddress = value;
                }
                else if (cmd.StartsWith("-gw-port="))
                {
                    if (ushort.TryParse(cmd.Substring("-gw-port=".Length), out var value))
                        mGatewayPort = value;
                }
                else if (cmd.StartsWith("-public-host="))
                {
                    var value = cmd.Substring("-public-host=".Length);
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (GetIPAddress(value) != null)
                            mPublicHost = value;
                    }
                }
            }
        }
        /// <summary>
        /// Displays info about command line usage
        /// </summary>
        private static void DisplayUsage()
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("-gw-host= : * Host or IP from the SRO server to connect");
            Console.WriteLine("-gw-port= : * Port from the Silkroad server to connect");

            Console.WriteLine("-bind-gw-port= : Port this proxy gonna use to behave as Gateway server (Recommended)");
            Console.WriteLine("-bind-ag-port= : Port this proxy gonna use to behave as Agent server");
            Console.WriteLine("-bind-dl-port= : Port this proxy gonna use to behave as Download server");

            Console.WriteLine("-public-host= : Public Host or IP you will use for external connections to your private network");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("> SimplestSilkroadFilter.exe -gw-host=192.168.1.121 -gw-port=15779 -bind-gw-port=15778");
            Console.WriteLine();
        }
        /// <summary>
        /// Mimic classic System("pause") from C++.
        /// </summary>
        private static void DisplayPause()
        {
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
        /// <summary>
        /// Find the IP used by this machine on the network
        /// </summary>
        public static List<string> GetMyLocalAddresses()
        {
            List<string> myLocalIps = new List<string>();
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    myLocalIps.Add(ip.ToString());
                    //return ip;
            return myLocalIps;
        }
        /// <summary>
        /// Try to solve and returns the host. Returns null if cannot be resolved
        /// </summary>
        public static IPAddress GetIPAddress(string HostOrAddress)
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(HostOrAddress);
                if (hostEntry.AddressList.Length > 0)
                    return hostEntry.AddressList[0];
            }
            catch { }
            return null;
        }
        /// <summary>
        /// Find an available port to bind
        /// </summary>
        public static int GetAvailablePort(AddressFamily addressFamily = AddressFamily.InterNetwork, SocketType socketType = SocketType.Stream, ProtocolType protocolType = ProtocolType.Tcp)
        {
            using (var socket = new Socket(addressFamily, socketType, protocolType))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }
        /// <summary>
		/// Get all the addresses this machine is using
		/// </summary>
		public static List<string> GetAllMyAddresses()
        {
            var ips = new List<string>
            {
                // Local
                "127.0.0.1"
            };

            // Private
            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                var hosts = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in hosts.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.ToString());
                        break;
                    }
                }

                // Public
                try
                {
                    ips.Add(new WebClient().DownloadString("http://ipinfo.io/ip"));
                } catch { }
            }

            // Return result
            return ips;
        }
        #endregion
    }
}
