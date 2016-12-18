using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Network
{
    // context for socket listener
    // manage all clients accepted
    public class ServerNetwork
    {
        public int ClientCount { get { return clientConnectorsDict.Count; } }

        public delegate void ServerNetworkClientConnectedHandler(IRemote remote);
        public delegate void ServerNetworkClientDisconnectedHandler(IRemote remote);
        public delegate void ServerNetworkClientMessageReceivedHandler(IRemote remote, Message msg);

        // todo event
        public ServerNetworkClientConnectedHandler OnClientConnected;
        public ServerNetworkClientDisconnectedHandler OnClientDisconnected;
        public ServerNetworkClientMessageReceivedHandler OnClientMessageReceived;

        // io thread pushes while user thread pops
        private readonly Dictionary<int, Connector> clientConnectorsDict = new Dictionary<int, Connector>();

        // io thread pushes while user thread pops
        private readonly SwapContainer<Queue<Connector>> toAddClientConnectors = new SwapContainer<Queue<Connector>>();
        
        // io or user thread pushes while user thread pops
        // currently, only user thread pushes
        private readonly SwapContainer<Queue<Connector>> toRemoveClientConnectors = new SwapContainer<Queue<Connector>>();

        // connectorId for next accepted client
        private int nextClientConnectorId = 1;

        //system socket
        private Socket listenSocket;

        public ServerNetwork(int port)
        {
            listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            listenSocket.Listen(10);
        }

        public void BeginAccept()
        {
            listenSocket.BeginAccept(OnAcceptedCallback, null);
        }

        public void Poll()
        {
            try
            {
                // todo heartbeat??

                ProcessClientConnectorsMessageQueue();
                RefreshClientList();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        internal void CloseClient(Connector connector, NetworkCloseMode mode)
        {
            if (connector == null)
            {
                Log.Warn("ServerNetwork.CloseClient socket is null");
                return;
            }

            lock (toRemoveClientConnectors.Lock)
            {
                toRemoveClientConnectors.In.Enqueue(connector);
                Log.Debug("ServerNetwork.CloseClient connectId={0}", mode, connector.Id);
            }
        }

        public void Dispose()
        {
            if (listenSocket != null)
            {
                listenSocket.Close();
            }

            Log.Debug("ServerSocket.Dispose");
        }

        // io thread schedules this entrance
        private void OnAcceptedCallback(IAsyncResult ar)
        {
            try
            {
                var clientSocket = listenSocket.EndAccept(ar);

                clientSocket.SendTimeout = 500;
                clientSocket.ReceiveTimeout = 500;
                clientSocket.NoDelay = true;

                var id = nextClientConnectorId;

                // to ensure atomic increment of nextClientConnectorId 
                // todo handle wraparound
                // Interlocked.Exchange is not supported on specific platforms, unity iOS full-AOT 
                Interlocked.Exchange(ref nextClientConnectorId, nextClientConnectorId + 1 < int.MaxValue ? nextClientConnectorId + 1 : 1);
                while (clientConnectorsDict.ContainsKey(nextClientConnectorId))
                {
                    Interlocked.Exchange(ref nextClientConnectorId, nextClientConnectorId + 1 < int.MaxValue ? nextClientConnectorId + 1 : 1);
                }

                var clientConnector = new Connector(clientSocket, id);

                lock (toAddClientConnectors.Lock)
                {
                    toAddClientConnectors.In.Enqueue(clientConnector);
                }

                // continue to accept
                listenSocket.BeginAccept(OnAcceptedCallback, null);
            }
            catch (Exception)
            {
                //if (OnAccepted != null) OnAccepted(acceptedResult.SetValue(null, -1, null, e));
            }
        }

        // user thread
        private void ProcessClientConnectorsMessageQueue()
        {
            foreach (var clientConnector in clientConnectorsDict.Values)
            {
                clientConnector.ProcessMessageQueue((c, msg) =>
                {
                    // notify upper layer, 
                    // that a new msg received
                    if (OnClientMessageReceived != null)
                    {
                        OnClientMessageReceived(c, msg);
                    }
                });
            }
        }

        // user thread
        private void RefreshClientList()
        {
            // do accept client connectors
            if (toAddClientConnectors.Out.Count == 0)
            {
                // consume what io thread produces
                toAddClientConnectors.Swap();

                foreach (var clientConnector in toAddClientConnectors.Out)
                {
                    // todo race condition, when add conn to clientConnectorsDict
                    if (clientConnectorsDict.ContainsKey(clientConnector.Id))
                    {
                        Log.Warn("ServerNetwork.RefreshClientList connector exist id={0}", clientConnector.Id);
                        return;
                    }

                    clientConnectorsDict.Add(clientConnector.Id, clientConnector);

                    // notify upper layer, 
                    // that a new client connected
                    if (OnClientConnected != null)
                    {
                        OnClientConnected(clientConnector);
                    }

                    // client.sysSocket.BeginReceive
                    clientConnector.BeginReceive();
                }

                toAddClientConnectors.Out.Clear();
            }

            // do remove client connectors
            if (toRemoveClientConnectors.Out.Count == 0)
            {
                toRemoveClientConnectors.Swap();
                foreach (var clientConnector in toRemoveClientConnectors.Out)
                {
                    if (clientConnectorsDict.ContainsKey(clientConnector.Id))
                    {
                        clientConnectorsDict.Remove(clientConnector.Id);
                        clientConnector.Close();
                        if (OnClientDisconnected != null)
                        {
                            OnClientDisconnected(clientConnector);
                        }
                    }
                }

                toRemoveClientConnectors.Out.Clear();
            }

            foreach (var client in clientConnectorsDict.Values)
            {
                client.Send();
                if (client.DefferedClose)
                {
                    // close any clients which failed sending data
                    CloseClient(client, NetworkCloseMode.DefferedClose); 
                }
            }
        }
    }

    public enum NetworkCloseMode
    {
        HeartbeatTimeout = 1,
        DefferedClose = 2,
        Dispose = 3,
    }
}
