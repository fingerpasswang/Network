using System;
using System.Net.Sockets;

namespace Network
{
    // context for connect socket
    // manage just one connector, which means local client 
    public class ClientNetwork
    {
        class ConnectAsyncResult
        {
            public Exception Ex;
            public Connector Conn;
        }

        public bool Connected { get { return connector != null && connector.Connected; } }

        public delegate void ClientNetworkConnectedHandler(ILocal local, Exception e);
        public delegate void ClientNetworkDisconnectedHandler();
        public delegate void ClientNetworkMessageReceivedHandler(Message msg);

        // todo use event
        public ClientNetworkConnectedHandler ConnectorConnected;
        public ClientNetworkDisconnectedHandler ConnectorDisconnected;
        public ClientNetworkMessageReceivedHandler ConnectorMessageReceived;

        // compared to serverNetwork
        // clientNetwork hold one connector for connect socket only
        private Connector connector;

        private ConnectAsyncResult defferedConnected = null;

        // ip:port for host
        private readonly string hostIp;
        private readonly int hostPort;

        // system socket
        private readonly Socket sysSocket;

        public ClientNetwork(string ip, int port)
        {
            hostIp = ip;
            hostPort = port;

            sysSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = 500,
                ReceiveTimeout = 500,
                NoDelay = true
            };
        }

        // block api
        public void Connect()
        {
            if (Connected)
                return;

            Log.Info("BeginReceive sysSocket.Connect(hostIp, hostPort);");
            try
            {
                sysSocket.Connect(hostIp, hostPort);
            }
            catch (Exception e)
            {
                Log.Error("ClientNetwork BeginReceive throw exp:{0}", e);
                defferedConnected = new ConnectAsyncResult()
                {
                    Ex = e,
                };

                return;
            }

            defferedConnected = new ConnectAsyncResult()
            {
                Conn = new Connector(sysSocket, 0),
            };
        }

        public void SendData(byte[] buffer)
        {
            if (connector == null || !connector.Connected)
            {
                return;
            }

            connector.Push(buffer, buffer.Length);
        }

        public void SendDatav(params byte[][] buffers)
        {
            if (connector == null || !connector.Connected)
            {
                return;
            }

            connector.Pushv(buffers);
        }

        public void Poll()
        {
            try
            {
                if (defferedConnected != null)
                {
                    connector = defferedConnected.Conn;
                    connector.BeginReceive();

                    // notify
                    if (ConnectorConnected != null)
                    {
                        ConnectorConnected(defferedConnected.Conn, defferedConnected.Ex);
                    }

                    defferedConnected = null;
                }

                RefreshMessageQueue();
                RefreshClient();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        // user thread
        public void Close()
        {
            if (!Connected)
            {
                return;
            }

            connector.Close();

            if (ConnectorDisconnected != null)
            {
                ConnectorDisconnected();
            }
        }

        // client only
        // todo
        public void SetClientRc4Key(string key)
        {
            if (connector != null)
            {
                //connector.RC4Key = key;
            }
        }

        public void Dispose()
        {
            //OnRecvMessage = null;

            // disconnect
            Close();
        }

        private void RefreshClient()
        {
            if (Connected)
            {
                if (!connector.DefferedClose)
                {
                    connector.Send();
                }

                // connector.Send() might cause a DefferedClose
                if (connector.DefferedClose)
                {
                    Close();
                }
            }
        }

        private void RefreshMessageQueue()
        {
            if (!Connected)
            {
                return;
            }

            connector.ProcessMessageQueue((c, msg) =>
            {
                if (ConnectorMessageReceived != null)
                {
                    ConnectorMessageReceived(msg);
                }
            });
        }
    }
}
