
namespace Network
{
    public class Message
    {
        public byte[] Buffer = null;
        public Message(byte[] buf)
        {
            Buffer = buf;
        }

        public override string ToString()
        {
            return string.Format("len={0} buff={1}", Buffer!=null?Buffer.Length:-1, BytesToString(Buffer));
        }

        public static string BytesToString(byte[] bytes)
        {
            if (bytes == null)
            {
                return "";
            }
            string s = "";
            for (int i = 0; i < bytes.Length; i++)
            {
                s += bytes[i];
                if (i != bytes.Length - 1)
                {
                    s += ",";
                }
            }
            return s;
        }
    }
}
