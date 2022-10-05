using Silkroad.Network;
using Silkroad.SecurityAPI;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace SimplestSilkroadFilter
{
    class Program
    {
        #region Private Members
        static IPAddress m_GatewayServerAddress;
        static ushort m_GatewayServerPort = 15779;
        static ushort m_AgentServerPort = 16779;

        static Dictionary<string, List<object[]>> m_AgentServerQueue = new Dictionary<string, List<object[]>>();
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
            if (m_GatewayServerAddress == null)
            {
                Console.WriteLine("Error! Silkroad server ip/host not found or cannot be resolved." + Environment.NewLine);
                ShowConsoleUsage();
                Console.WriteLine("* Press any key to exit . . .");
                Console.ReadKey();
                return;
            }

            // Initialize gateway proxy
            AsyncServer gwServer = new AsyncServer();
            gwServer.SetProxy(m_GatewayServerAddress.ToString(), m_GatewayServerPort);
            // Log gateway connections
            gwServer.OnProxyConnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection established (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            gwServer.OnProxyDisconnected += (_s, _e) => {
                Console.WriteLine("Gateway Server: Connection finished (" + _e.Proxy.Server.Socket.LocalEndPoint + ")");
            };
            // Server Login Response
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
                    // Create and add this connection to the agent server queue control
                    if (!m_AgentServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                    {
                        connections = new List<object[]>();
                        m_AgentServerQueue[clientIP] = connections;
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
                    p.WriteAscii("127.0.0.1");
                    p.WriteUShort(m_AgentServerPort);
                    _e.Proxy.Client.Send(p);
                    // Avoid send this packet to client
                    _e.CancelTransfer = true;
                }
            });

            // Start gateway server
            Console.WriteLine("Initializing Gateway Server on port " + m_GatewayServerPort + "...");
            if (gwServer.Start(m_GatewayServerPort))
            {
                Console.WriteLine("Gateway Server started successfully!");

                // Initialize agent proxy
                AsyncServer agServer = new AsyncServer();

                // Control dynamic proxy connections
                agServer.OnProxyConnection += (_s, _e) =>
                {
                    var clientIP = ((IPEndPoint)_e.Proxy.Client.Socket.RemoteEndPoint).Address.ToString();

                    // Check connections controller
                    if (m_AgentServerQueue.TryGetValue(clientIP, out List<object[]> connections))
                    {
                        // Check all connections from this IP and remove the old ones
                        object[] clientQueue = null;
                        for (int i = 0; i < connections.Count; i++)
                        {
                            Stopwatch connectionTime = (Stopwatch)connections[i][2];
                            // Remove connections longer than one minute
                            if (connectionTime.Elapsed.Minutes > 1)
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
                // Attach server opcodes to copy packet
                var handlerCopyPacket = new AsyncServer.PacketTransferEventHandler((_s, _e) => {
                    // Copy packet and send it as a new opcode
                    var p = new Packet(0xF00D, _e.Packet.Encrypted, _e.Packet.Massive);
                    p.WriteUShort(_e.Packet.Opcode);
                    p.WriteByteArray(_e.Packet.GetBytes());
                    _e.Proxy.Client.Send(p);
                });
                agServer.RegisterServerPacketHandler(0x3015, handlerCopyPacket); // Spawn entity
                agServer.RegisterServerPacketHandler(0x3016, handlerCopyPacket); // Despawn entity
                agServer.RegisterServerPacketHandler(0x3017, handlerCopyPacket); // Group Spawn begin
                agServer.RegisterServerPacketHandler(0x3018, handlerCopyPacket); // Group Spawn end
                agServer.RegisterServerPacketHandler(0x3019, handlerCopyPacket); // Group Spawn data

                // Attach client opcode to build packet
                var handlerBuildPacket = new AsyncServer.PacketTransferEventHandler((_s, _e) => {
                    // Build packet and send it
                    var opcode = _e.Packet.ReadUShort();
                    var data = _e.Packet.ReadByteArray(_e.Packet.RemainingRead());
                    _e.Proxy.Server.Send(new Packet(opcode, _e.Packet.Encrypted, _e.Packet.Massive, data));
                    // Avoid proxy this packet
                    _e.CancelTransfer = true;
                });
                agServer.RegisterClientPacketHandler(0xF00D, handlerBuildPacket); // Custom output

                // Start agent server
                Console.WriteLine("Initializing Agent Server on port " + m_AgentServerPort + "...");
                if (agServer.Start(m_AgentServerPort))
                {
                    Console.WriteLine("Agent Server started successfully!");
                }
            }

            // Reading ENTER to exit
            Console.WriteLine("* Press ESCAPE anytime to exit . . .");
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
                if (cmd.StartsWith("-ip="))
                {
                    var address = GetIPAddress(args[i].Substring(4));
                    if (address != null)
                        m_GatewayServerAddress = address;
                }
                else if (cmd.StartsWith("-port="))
                {
                    if (ushort.TryParse(args[i].Substring(6), out ushort port))
                        m_GatewayServerPort = port;
                }
                else if (cmd.StartsWith("-ag-port="))
                {
                    if (ushort.TryParse(args[i].Substring(9), out ushort port))
                        m_AgentServerPort = port;
                }
            }
        }
        /// <summary>
        /// Shows application usage at console
        /// </summary>
        static void ShowConsoleUsage()
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("-ip : IP or HOST from the Silkroad to connect.");
            Console.WriteLine("-port : Gateway port from your Silkroad to connect.");
            Console.WriteLine("-ag-port : Optional port used as agent filter server.");
            Console.WriteLine();
            Console.WriteLine("Example as local server:");
            Console.WriteLine("> SimplestSilkroadFilter.exe -ip=127.0.0.1 -port=15779");
            Console.WriteLine();
        }
        /// <summary>
        /// Get IP address
        /// </summary>
        static IPAddress GetIPAddress(string HostNameOrAddress)
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(HostNameOrAddress);
                if (hostEntry.AddressList.Length > 0)
                    return hostEntry.AddressList[0];
            }
            catch { }
            return null;
        }
        #endregion
    }
}
