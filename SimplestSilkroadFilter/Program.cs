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
        private static ushort mBindPort;
        private static string mPublicHost;

        private static string mGatewayAddress;
        private static ushort mGatewayPort;


        private static Dictionary<string, List<object[]>> mAgentServerQueue = new Dictionary<string, List<object[]>>();
        private static TimeSpan mAgentServerQueueTimeLimit = new TimeSpan(0,0,10);
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
                Console.WriteLine("Error! Host not found or cannot be resolved." + Environment.NewLine);
                DisplayUsage();
                DisplayPause();
                return;
            }
            var localIPAddress = GetMyLocalAddress().ToString();
            var bindPortForAgent = GetAvailablePort();
            if (mPublicHost == null)
                mPublicHost = GetAllMyAddresses().Last();
            

            #region Gateway Server Setup
            // Initialize gateway proxy
            AsyncServer gwServer = new AsyncServer();
            gwServer.SetProxy(mGatewayAddress, mGatewayPort);
            // Log gateway connections
            gwServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            gwServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            // Login Response
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
                    if (clientIP != localIPAddress)
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

                    Console.WriteLine("Gateway Server: Redirecting connection from (" + _e.Proxy.Client.Socket.LocalEndPoint + ")");
                    // Redirect client to the local agent server
                    var p = new Packet(0xA102);
                    p.WriteByte(1); // success
                    p.WriteUInt(queueId);
                    p.WriteAscii(agentServerIP);
                    p.WriteUShort(bindPortForAgent);
                    _e.Proxy.Client.Send(p);

                    // Avoid send this packet already handled to client
                    _e.CancelTransfer = true;
                }
            });

            // Run Gateway Server
            Console.WriteLine("Initializing Gateway Proxy Server on port " + mBindPort + "...");
            try
            {
                gwServer.Start(mBindPort);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                DisplayPause();
                return;
            }
            Console.WriteLine("Gateway Server started successfully!");
            #endregion

            #region Agent Server Setup
            // Initialize agent proxy
            AsyncServer agServer = new AsyncServer();

            // Agent server queue control - WHY? To avoid IP change (kinda of exploit) at login
            agServer.OnProxyConnection += (_s, _e) =>
            {
                var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();
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

            // Log agent connections
            agServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Agent Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            agServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Agent Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };

            // Start agent server
            Console.WriteLine("Initializing Agent Server on port " + bindPortForAgent + "...");
            try
            {
                agServer.Start(bindPortForAgent);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                DisplayPause();
                return;
            }
            Console.WriteLine("Agent Server started successfully!");
            #endregion

            // Reading ENTER to exit
            Console.WriteLine("Press ESCAPE anytime to exit . . .");
            while (Console.ReadKey(false).Key != ConsoleKey.Escape) ;
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
                if (cmd.StartsWith("-bind-port="))
                {
                    if(ushort.TryParse(cmd.Substring("-bind-port=".Length), out var value))
                        mBindPort = value;
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
                        var address = GetIPAddress(value);
                        if (address != null)
                            mPublicHost = address.ToString();
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
            Console.WriteLine("-bind-port : Port this proxy gonna use to behave as Gateway");
            Console.WriteLine("-gw-host : Host or IP from the Silkroad server to connect");
            Console.WriteLine("-gw-port : Port from the Silkroad server to connect");
            Console.WriteLine("-public-host : (Optional) Host you'll use to redirect connections outside your local machine");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("> SimplestSilkroadFilter.exe -bind-port=15777 -gw-host=192.168.1.121 -gw-port=15779");
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
        public static IPAddress GetMyLocalAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip;
            return null;
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
