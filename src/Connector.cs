using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Network
{
    public interface IRemote
    {
        string RemoteIp { get; }
        int RemotePort { get; }
        int Id { get; }

        int Push(byte[] buffer, int len, int offset);
        int PushBegin(int len);
        int PushMore(byte[] buffer, int len, int offset);
    }

    public interface ILocal
    {
        string RemoteIp { get; }
        int RemotePort { get; }
    }

    internal class Connector : IRemote, ILocal
    {
        private const int HeadLen = 4;

        // system socket
        private Socket sysSocket;

        // todo change to bufferlist
        // todo not ensure thread-safe yet
        private ConnectorBuffer receiveBuffer = new ConnectorBuffer();
        private ConnectorBuffer sendBuffer = new ConnectorBuffer();

        private readonly SwapContainer<Queue<Message>> msgQueue = new SwapContainer<Queue<Message>>();

        // todo not implemented yet 
        private RC4 rc4Read;
        private RC4 rc4Write;

        // will be set to true when exception or ServerNetwork.Dispose,
        // after which Network will close this connection
        internal bool DefferedClose { get; private set; }
        internal bool Connected { get; set; }

        public int Id { get; private set; }

        public string RemoteIp
        {
            get
            {
                if (sysSocket != null)
                {
                    var ipEndPoint = sysSocket.RemoteEndPoint as IPEndPoint;
                    if (ipEndPoint != null)
                    {
                        return ipEndPoint.Address.ToString();
                    }
                }
                return "";
            }
        }

        public int RemotePort
        {
            get
            {
                if (sysSocket != null)
                {
                    var ipEndPoint = sysSocket.RemoteEndPoint as IPEndPoint;
                    if (ipEndPoint != null)
                    {
                        return ipEndPoint.Port;
                    }
                }
                return 0;
            }
        }

        public delegate void ConnectorMessageHandler(IRemote remote, Message msg);

        public Connector(Socket s, int id)
        {
            sysSocket = s;
            Id = id;
            Connected = true;
        }

        public int PushBegin(int len)
        {
            if (!Connected)
            {
                return -1;
            }

            var headData = new byte[HeadLen];

            Helper.Int32ToByteArray(len, headData, 0);

            sendBuffer.PushData(headData, headData.Length);

            return HeadLen;
        }

        // todo in case inputs an ownerless buffer, sendBuffer should be converted to buffer list
        public int PushMore(byte[] buffer, int len, int offset = 0)
        {
            if (rc4Write != null) 
            {
                rc4Write.Encrypt(buffer, len, offset);
            }

            sendBuffer.PushData(buffer, len, offset);

            return len;
        }

        public int Push(byte[] buffer, int len, int offset = 0)
        {
            if (!Connected)
            {
                return -1;
            }
            if (offset + len >= buffer.Length)
            {
                return -2;
            }

            //rc4 encryption
            if (rc4Write != null) 
            {
                rc4Write.Encrypt(buffer, len, offset);
            }

            var headData = new byte[HeadLen];

            Helper.Int32ToByteArray(len, headData, 0);

            sendBuffer.PushData(headData, headData.Length);
            sendBuffer.PushData(buffer, len, offset);

            return buffer.Length + HeadLen;
        }

        public int Pushv(params byte[][] buffers)
        {
            if (!Connected)
            {
                return -1;
            }

            int size = 0;
            foreach (var buffer in buffers)
            {
                size += buffer.Length;
            }

            // rc4 encryption
            if (rc4Write != null) 
            {
                foreach (var buffer in buffers)
                {
                    rc4Write.Encrypt(buffer, buffer.Length);
                }
            }

            var headData = new byte[HeadLen];

            Helper.Int32ToByteArray(size, headData, 0);

            sendBuffer.PushData(headData, headData.Length);

            foreach (var buffer in buffers)
            {
                sendBuffer.PushData(buffer, buffer.Length);
            }

            return size + HeadLen;
        }

        public void Send()
        {
            // no data in sendBuffer
            // neednt to send data
            if (sendBuffer.Length <= 0)
            {
                return;
            }

            try
            {
                //SysSocket.BeginSend(sendBuffer.ToArray(), 0, sendBuffer.Count, SocketFlags.None, null, null);
                int realSendLen = sysSocket.Send(sendBuffer.Buffer, sendBuffer.Start, sendBuffer.Length, SocketFlags.None);

                // todo support for statistics here

                if (realSendLen == sendBuffer.Length)
                {
                    // todo add shrink capablity to buffer
                    sendBuffer.Reset();
                }
                else
                {
                    sendBuffer.Consume(realSendLen);
                }
            }
            catch (SocketException e)
            {
                // WSAEWOULDBLOCK
                // occurs when debug server group
                if (e.ErrorCode != 10035) 
                {
                    Log.Debug("Connector.SendData error connectId={0} errorCode={1} msg={2}", Id, e.ErrorCode, e.Message);
                }

                // todo specially, when the connector remote is frontend-client, any errors would cause DefferedClose
                // this can be done in gate
                if (e.ErrorCode == 10054 || e.ErrorCode == 10053 || e.ErrorCode == 10058)
                {
                    // 10054 stands for a close of remote
                    sendBuffer.Reset();
                    DefferedClose = true;
                }
            }
            catch (Exception e)
            {
                sendBuffer.Reset();
                Log.Warn("Connector.SendData error={0}", e);
                DefferedClose = true;
            }
        }

        public void BeginReceive()
        {
            Receive();
        }

        public void ProcessMessageQueue(ConnectorMessageHandler msgProcessor)
        {
            if (!Connected)
            {
                return;
            }

            if (msgQueue.Out.Count == 0)
            {
                // would block for accquiring lock
                msgQueue.Swap();
            }

            while (msgQueue.Out.Count > 0)
            {
                var msg = msgQueue.Out.Dequeue();

                msgProcessor(this, msg);
            }
        }

        public void Close()
        {
            Connected = false;

            if (sysSocket != null)
            {
                sysSocket.Close();
                sysSocket = null;
            }

            receiveBuffer.Dispose();
            sendBuffer.Dispose();

            receiveBuffer = null;
            sendBuffer = null;
        }

        private void Receive()
        {
            if (!Connected)
            {
                return;
            }
            try
            {
                if (receiveBuffer.EnsureFreeSpace(1))
                {
                    sysSocket.BeginReceive(receiveBuffer.Buffer, receiveBuffer.Position, receiveBuffer.Free, SocketFlags.None, OnReceivedCallback, this);
                }
                else
                {
                    DefferedClose = true;
                }
            }
            catch (SocketException e)
            {
                Log.Debug("Connector.Receive error={0}", e.Message);
            }
            catch (Exception e)
            {
                Log.Warn("Connector.Receive error={0}", e);
            }
        }

        // io thread schedules this entrance
        private void OnReceivedCallback(IAsyncResult ar)
        {
            int bytesRead = 0;
            try
            {
                if (sysSocket != null)
                {
                    bytesRead = sysSocket.EndReceive(ar);
                }
            }
            catch (ObjectDisposedException e)
            {
                Log.Debug("Connector.ReceiveCallback objectDisposedException connectId={0}", Id);
                DefferedClose = true;
                return;
            }
            catch (SocketException e)
            {
                Log.Debug("Connector.ReceiveCallback connectId={0} errorCode={1} errorMessage={2}", Id, e.ErrorCode, e.Message);
                if (e.ErrorCode == 10054 || e.ErrorCode == 10053 || e.ErrorCode == 10058)
                {
                    // todo specially, when the connector remote is frontend-client, any errors would cause DefferedClose
                    // this can be done in gate
                    DefferedClose = true;
                }
                return;
            }
            catch (Exception)
            {
                // unknown errors
                Log.Error("Connector.ReceiveCallback exception connectId={0}", Id);
                DefferedClose = true;
                return;
            }

            if (bytesRead == 0)
            {
                Log.Debug("Connector.ReceiveCallback  read is 0 connectId={0}", Id);
                // todo specially, when the connector remote is frontend-client, any errors would cause DefferedClose
                // this can be done in gate

                // remember this return, or it will go to a infinity recursion
                return;
            }

            receiveBuffer.Produce(bytesRead);

            // todo support for statistics here

            while (receiveBuffer.Length >= HeadLen)
            {
                // todo a strange bug occurs here ever
                int size = BitConverter.ToInt32(receiveBuffer.Buffer, receiveBuffer.Start);

                if (size < 0)
                {
                    Log.Warn("Connector.ReceiveCallback size={0} id={1} buffer={2} bytesRead={3}", size, Id, receiveBuffer, bytesRead);
                    break;
                }

                if (receiveBuffer.Length >= size + HeadLen)
                {
                    byte[] destBuffer = null;

                    destBuffer = new byte[size];
                    Buffer.BlockCopy(receiveBuffer.Buffer, receiveBuffer.Start + HeadLen, destBuffer, 0, size);

                    // decode
                    if (rc4Read != null) 
                    {
                        rc4Read.Encrypt(destBuffer, size);
                    }

                    // todo add shrink capablity to buffer
                    receiveBuffer.Reset();

                    try
                    {
                        MessageQueueEnqueue(destBuffer);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }
                }
                else
                {
                    // remained data is still uncoming
                    // wait for next callback,
                    // since tcp is a stream-based protocol
                    break;
                }
            }
            Receive();
        }

        // io thread entrance
        private void MessageQueueEnqueue(byte[] buf)
        {
            var msg = new Message(buf);

            // msgQueue will be polled when user thread invoke  
            lock (msgQueue.Lock) 
            {
                msgQueue.In.Enqueue(msg);
            }
        }
    }
}
