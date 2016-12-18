namespace Network
{
    // swap message queue
    // a container, which retrieve item on one thread (io thread or user thread)
    // while pop item on the other thread ()
    class SwapContainer<T>
        where T:new()
    {
        public T In = new T();
        public T Out = new T();

        public readonly object Lock = new object();

        // thraed-safe
        public void Swap()
        {
            // need accquiring lock first
            // swap in/out containers
            lock (Lock)
            {
                var temp = In;
                In = Out;
                Out = temp;
            }
        }
    }
}
