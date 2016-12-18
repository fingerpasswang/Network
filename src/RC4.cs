using System;
using System.Text;

namespace Network
{
    class RC4
    {
        private readonly Byte[] mBox;
        private static readonly Encoding Encode = Encoding.Default;
        private const int mBoxLength = 256;
        int x;
        int y;
        public RC4(string pass)
        {
            mBox = GetKey(Encode.GetBytes(pass));
            x = 0;
            y = 0;
        }

        public void Encrypt(Byte[] data, int len, int offset = 0)
        {
            if (data == null)
                return;
            // encryption
            for (int i = offset; i < offset+len && i < data.Length; i++)
            {
                x = (x + 1) % mBoxLength;
                y = (y + mBox[x]) % mBoxLength;
                Byte temp = mBox[x];
                mBox[x] = mBox[y];
                mBox[y] = temp;
                Byte b = mBox[(mBox[x] + mBox[y]) % mBoxLength];
                data[i] ^= b;
            }
        }

        // disorder the pass
        private static Byte[] GetKey(Byte[] pass)
        {
            Byte[] mBox = new Byte[mBoxLength];

            for (int i = 0; i < mBoxLength; i++)
            {
                mBox[i] = (Byte)i;
            }
            int j = 0;
            for (int i = 0; i < mBoxLength; i++)
            {
                j = (j + mBox[i] + pass[i % pass.Length]) % mBoxLength;
                Byte temp = mBox[i];
                mBox[i] = mBox[j];
                mBox[j] = temp;
            }
            return mBox;
        }
    }
}
