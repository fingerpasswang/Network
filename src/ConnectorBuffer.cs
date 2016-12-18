using System;

namespace Network
{
    class ConnectorBuffer : IDisposable
    {
        private byte[] bufferInner = new byte[1024];

        private int position;
        private int begin;

        public byte[] Buffer
        {
            get { return bufferInner; }
        }
        public int Start
        {
            get { return begin; }
        }
        public int Position
        {
            get { return position; }
        }
        public int Length
        {
            get { return position - begin; }
        }
        public int Free
        {
            get { return bufferInner.Length - position; }
        }

        public void PushData(byte[] data, int size, int offset = 0)
        {
            CheckResize(size);

            System.Buffer.BlockCopy(data, offset, bufferInner, position, size);
            position += size;
        }

        public void Consume(int offset)
        {
            // todo check
            begin += offset;
        }

        public void Produce(int offset)
        {
            // todo check
            position += offset;
        }

        public void Reset()
        {
            position = 0;
            begin = 0;
        }

        public bool EnsureFreeSpace(int free)
        {
            CheckResize(free);

            return true;
        }

        void CheckResize(int size)
        {
            int newSize = bufferInner.Length;
            while (newSize - position < size)
            {
                // todo limit check
                newSize *= 2;
            }

            if (newSize > bufferInner.Length)
            {
                byte[] tmp = new byte[newSize];
                if (position > 0)
                {
                    if (position <= bufferInner.Length)
                    {
                        var buffLen = position - begin;

                        System.Buffer.BlockCopy(bufferInner, begin, tmp, 0, buffLen);

                        begin = 0;
                        position = buffLen;
                    }
                    else
                    {
                        //Log.Error("AddData fail endPoint={0} sendBufferLen={1}", endPoint, sendBuffer.Length);
                    }
                }
                bufferInner = tmp;
            }
        }

        public void Dispose()
        {
            Reset();
            bufferInner = null;
        }

        public override string ToString()
        {
            return string.Format("{{bufferInner.Len:{0} position:{1} begin:{2}}}", bufferInner.Length,
                position, begin);
        }
    }
}
