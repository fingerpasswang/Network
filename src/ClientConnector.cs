using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Network
{
    public interface ISocketClient
    {
        
    }

    class ClientConnector
    {
        protected const int HeadLen = 4;

        // system socket
        private Socket sysSocket;

        private ConnectorBuffer receiveBuffer = new ConnectorBuffer();
        private ConnectorBuffer sendBuffer = new ConnectorBuffer();

        private readonly SwapContainer<Queue<Message>> msgQueue = new SwapContainer<Queue<Message>>();

        protected RC4 rc4Read;
        protected RC4 rc4Write;

        public bool DefferedClose { get; protected set; } //如果出了异常等，会把这个值设true，然后在外层被断开；关闭服务器时也会设置
        public bool Connected { get; set; }

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

        public ClientConnector(Socket s, int id)
        {
            sysSocket = s;
            Id = id;
        }

        public int Push(byte[] buffer, int size)
        {
            if (!Connected)
            {
                return -1;
            }

            if (rc4Write != null) //发送前做rc4加密
            {
                rc4Write.Encrypt(buffer, size);
            }

            var headData = new byte[HeadLen];

            Helper.Int32ToByteArray(size, headData, 0);

            sendBuffer.PushData(headData, headData.Length);
            sendBuffer.PushData(buffer, buffer.Length);

            return size + HeadLen;
        }

        public void Send()
        {
            if (sendBuffer.Length <= 0)//没有任何数据
            {
                return;
            }

            try
            {
                //SysSocket.BeginSend(sendBuffer.ToArray(), 0, sendBuffer.Count, SocketFlags.None, null, null);
                int realSendLen = sysSocket.Send(sendBuffer.Buffer, sendBuffer.Start, sendBuffer.Length, SocketFlags.None);
                //LastTickSend += realSendLen;

                if (realSendLen == sendBuffer.Length)
                {
                    //if (sendBuffer.Length >= 1024 * 4)
                    //{
                    //    if (endPoint < sendBuffer.Length / 4) //一直达不到25%使用率，会释放掉一般空间
                    //    {
                    //        shrinkHint++;
                    //        if (shrinkHint == 5)
                    //        {
                    //            sendBuffer = new byte[sendBuffer.Length / 4];
                    //            shrinkHint = 0;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        shrinkHint = 0;
                    //    }
                    //}
                    sendBuffer.Reset();
                }
                else
                {
                    sendBuffer.Pop(realSendLen);
                }
            }
            catch (SocketException e)
            {
                if (e.ErrorCode != 10035) //WSAEWOULDBLOCK，这个错误调试时会出现
                {
                    Log.Debug("UXSocket.SendData error connectId={0} errorCode={1} msg={2}", Id, e.ErrorCode, e.Message);
                }

                if (UserId > 0 || e.ErrorCode == 10054 || e.ErrorCode == 10053 || e.ErrorCode == 10058)//UserId > 0主要是客户端，10054是远程强制关闭的情况
                {
                    sendBuffer.Reset();
                    DefferedClose = true;
                }
            }
            catch (Exception e)
            {
                sendBuffer.Reset();
                Log.Warn("UXSocket.SendData error={0}", e);
                DefferedClose = true;
            }
        }

        public void BeginReceive()
        {
            Receive();
        }

        public void ProcessMessageQueue(ProcessMessage processMessage)
        {
            if (!Connected)
            {
                return;
            }
            if (msgQueue.Out.Count == 0)
            {
                // wold block for aaccquiring lock
                msgQueue.Swap(); 
            }

            while (msgQueue.Out.Count > 0)
            {
                var msg = msgQueue.Out.Dequeue();

                processMessage(this, msg);
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

            readBuffer = null;
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
                    sysSocket.BeginReceive(receiveBuffer.Buffer, receiveBuffer.Start, receiveBuffer.Free, SocketFlags.None, OnReceivedCallback, this);
                }
                else
                {
                    DefferedClose = true;
                }
            }
            catch (SocketException e)
            {
                Log.Debug("UXSocket.Receive error={0}", e.Message);
            }
            catch (Exception e)
            {
                Log.Warn("UXSocket.Receive error={0}", e);
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
                Log.Debug("UXSocket.ReceiveCallback objectDisposedException connectId={0}", Id);
                DefferedClose = true;
                return;
            }
            catch (SocketException e)
            {
                Log.Debug("UXSocket.ReceiveCallback connectId={0} errorCode={1} errorMessage={2}", Id, e.ErrorCode, e.Message);
                if (/*UserId > 0 || */e.ErrorCode == 10054 || e.ErrorCode == 10053 || e.ErrorCode == 10058) //socket断开等严重错误，强制断开
                {
                    DefferedClose = true;
                }
                return;
            }
            catch (Exception)
            {
                Log.Error("UXSocket.ReceiveCallback exception connectId={0}", Id);//这些错误是未知
                DefferedClose = true;
                return;
            }

            if (bytesRead == 0)
            {
                Log.Debug("UXSocket.ReceiveCallback  read is 0 connectId={0} userId={1}", Id, UserId);
                //if (UserId > 0) //只会尝试断开外部连接
                //{
                //    WaitClose = true;
                //}
                //需要return，因为mono下receive会同步回调，导致卡死
                return;
            }

            receiveBuffer.Peek(bytesRead);
            //LastTickReceive += bytesRead;

            while (receiveBuffer.Length >= HeadLen)
            {
                int size = BitConverter.ToInt32(receiveBuffer.Buffer, receiveBuffer.Start);//todo 这里出过异常 gate

                if (size < 0)
                {
                    Log.Warn("UXSocket.ReceiveCallback size={0} id={1} pkgBufferIndex={2} pkgBufferFinish={3} pkgBufferLen={4} bytesRead={5}", size, Id, readBufferEnd, readBufferBegin, readBuffer.Length, bytesRead);
                    break;
                }

                if (receiveBuffer.Length >= size + HeadLen)
                {
                    byte[] destBuffer = null;

                    destBuffer = new byte[size];
                    Buffer.BlockCopy(receiveBuffer.Buffer, receiveBuffer.Start + HeadLen, destBuffer, 0, size);

                    if (rc4Read != null) //做数据拆包前解密数据包
                    {
                        rc4Read.Encrypt(destBuffer, size);
                    }

                    receiveBuffer.Pop(size + HeadLen);

                    //if (readBufferBegin >= readBuffer.Length / 2) //大于一半的buffer数据已经处理后把不用的数据清理一次
                    //{
                    //    readBufferEnd -= readBufferBegin;
                    //    Buffer.BlockCopy(readBuffer, readBufferBegin, readBuffer, 0, readBufferEnd);
                    //    readBufferBegin = 0;
                    //}

                    try
                    {
                        MessageQueueEnqueue(destBuffer, size);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }
                }
                else
                {
                    // 如果剩下的数据还没有形成完整的包，等待下次接受后继续处理
                    break;
                }
            }
            Receive();
        }

        private void MessageQueueEnqueue(byte[] buf, int len)
        {
            var msg = new Message(buf, len);
            lock (msgQueue.Lock) // 消息被压入队列，供主线程的 RefreshMessageQueue 处理
            {
                msgQueue.In.Enqueue(msg);
            }
        }
    }
}
