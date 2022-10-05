using Silkroad.SecurityAPI;

using System;
using System.Net.Sockets;
using System.Diagnostics;

namespace Silkroad.Network
{
    /// <summary>
    /// Async socket handling all silkroad packet stuffs
    /// </summary>
    public class AsyncClient
    {
        #region Private Members
        /// <summary>
        /// Storage packet buffer
        /// </summary>
        private TransferBuffer m_Buffer { get; set; } = new TransferBuffer(8192);
        #endregion

        #region Public Properties
        /// <summary>
        /// Socket handling the connection
        /// </summary>
        public Socket Socket { get; private set; }
        /// <summary>
        /// Security to send every packet
        /// </summary>
        public Security Security { get; set; } = new Security();
        /// <summary>
        /// Check if the client is connected to a remote device
        /// </summary>
        public bool IsConnected { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Create a client object ready to interact with silkroad server
        /// </summary>
        public AsyncClient()
        {

        }
        /// <summary>
        /// Create a client object ready to interact with silkroad server
        /// </summary>
        public AsyncClient(Socket Socket)
        {
            this.Socket = Socket;
            this.IsConnected = IsSocketConnected(Socket);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Create a remote connection in asynchronous mode
        /// </summary>
        public void BeginConnect(string Host, int Port, long MilisecondsTimeOut = 5000)
        {
            // Connect to remote device  
            try
            {
                // Create a TCP/IP socket context
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                var timeOut = Stopwatch.StartNew();
                // Connect to the remote endpoint
                Socket.BeginConnect(Host, Port, (asyncResult) => {
                    try
                    {
                        // Complete the connection
                        Socket.EndConnect(asyncResult);

                        // Check if the maximum connecting time is reached
                        timeOut.Stop();
                        if (timeOut.ElapsedMilliseconds > MilisecondsTimeOut)
                        {
                            // Connection timed out
                            throw new SocketException(10060);
                        }

                        // call event
                        _OnConnect();

                        // Start receiving data from remote
                        BeginReceive();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        /// <summary>
        /// Start receiving asynchronous data from remote device
        /// </summary>
        public void BeginReceive()
        {
            try
            {
                // Begin receiving the data from the remote device
                Socket.BeginReceive(m_Buffer.Buffer, 0, m_Buffer.Buffer.Length, SocketFlags.None, (AsyncResult) => {

                    if (!IsSocketConnected(Socket))
                    {
                        // Close connection
                        Close();
                        return;
                    }

                    // Read data from the remote device
                    var bytesRead = Socket.EndReceive(AsyncResult);

                    if (bytesRead > 0)
                    {
                        // There might be more data, so store the data received so far
                        Security.Recv(m_Buffer.Buffer, 0, bytesRead);

                        // All the data has arrived, process it
                        var packets = Security.TransferIncoming();

                        // Just in case
                        if(packets != null)
                            foreach (var p in packets)
                                // call event
                                _OnPacketReceived(p);

                        // Send packets collected
                        BeginSend();
                    }

                    // Get the rest of the data
                    BeginReceive();

                }, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        /// <summary>
        /// Disconnect the connection and clean resources
        /// </summary>
        public void Close()
        {
            if(IsConnected)
            {
                // call event
                _OnDisconnect();

                // Try to shutdown
                if (Socket.Connected)
                    Socket.Shutdown(SocketShutdown.Both);

                // Release it
                Socket.Close();
            }
        }
        /// <summary>
        /// Send packet to the remote device
        /// </summary>
        public void Send(Packet p)
        {
            // Send packet through security
            Security.Send(p);
            
            // call event
            _OnPacketSent(p);

            // Send the queue inmediatly
            BeginSend();
        }
        /// <summary>
        /// Start sending asynchronous data to remote device
        /// </summary>
        public void BeginSend()
        {
            // Retrieve the packets collected
            var buffers = Security.TransferOutgoing();

            // Avoid overusing it
            if (buffers == null)
                return;
            try
            {
                // Send one by one
                foreach (var kvp in buffers)
                {
                    // Send the packet buffer
                    Socket.BeginSend(kvp.Key.Buffer, kvp.Key.Offset, kvp.Key.Size, SocketFlags.None, (_asyncResult) =>
                    {
                        try
                        {
                            // Complete sending the data to the remote device
                            Socket.EndSend(_asyncResult);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    }, null);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        #endregion

        #region Private Helpers
        /// <summary>
        /// Check if the socket is connected
        /// </summary>
        private bool IsSocketConnected(Socket Socket)
        {
            try
            {
                return !(Socket.Poll(1, SelectMode.SelectRead) && Socket.Available == 0);
            }
            catch { return false; }
        }
        #endregion

        #region Events
        public delegate void PacketReceivedEventHandler(object sender, PacketReceivedEventArgs e);
        public class PacketReceivedEventArgs : EventArgs
        {
            public Packet Packet { get; }
            internal PacketReceivedEventArgs(Packet Packet)
            {
                this.Packet = Packet;
            }
        }
        /// <summary>
        /// Called when a server packet has been received
        /// </summary>
        public event PacketReceivedEventHandler OnPacketReceived;
        private void _OnPacketReceived(Packet Packet)
        {
            OnPacketReceived?.Invoke(this, new PacketReceivedEventArgs(Packet));
        }
        /// <summary>
        /// Called when a client packet has been sent to the server
        /// </summary>
        public event PacketReceivedEventHandler OnPacketSent;
        private void _OnPacketSent(Packet Packet)
        {
            OnPacketSent?.Invoke(this, new PacketReceivedEventArgs(Packet));
        }
        /// <summary>
        /// Called when the connection is established
        /// </summary>
        public event ConnectEventHandler OnConnect;
        public delegate void ConnectEventHandler(object sender, EventArgs e);
        private void _OnConnect()
        {
            // Update flag
            IsConnected = true;
            
            OnConnect?.Invoke(this, EventArgs.Empty);
        }
        /// <summary>
        /// Called when the connection is lost for any reason
        /// </summary>
        public event DisconnectEventHandler OnDisconnect;
        public delegate void DisconnectEventHandler(object sender, EventArgs e);
        private void _OnDisconnect()
        {
            // Update flag
            IsConnected = false;
            
            OnDisconnect?.Invoke(this, EventArgs.Empty);
        }
        #endregion
    }
}