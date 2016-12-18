
namespace Network
{
    public static class LogHandlerRegister
    {
        public delegate void LogHandler(string msg);

        public static LogHandler InfoHandler;
        public static LogHandler DebugHandler;
        public static LogHandler WarnHandler;
        public static LogHandler ErrorHandler;
    }

    // internal log module 
    // log handlers can be injected externally
    // todo not ensure thread-safe yet
    internal static class Log
    {
        enum LogLevel
        {
            Info,
            Debug,
            Warn,
            Error,
        }

        internal static void Info(string formater, params object[] args)
        {
            LogInternal(LogLevel.Info, string.Format(formater, args));
        }
        internal static void Info(object msg)
        {
            LogInternal(LogLevel.Info, msg.ToString());
        }
        internal static void Debug(string formater, params object[] args)
        {
            LogInternal(LogLevel.Debug, string.Format(formater, args));
        }
        internal static void Debug(object msg)
        {
            LogInternal(LogLevel.Debug, msg.ToString());
        }
        internal static void Warn(string formater, params object[] args)
        {
            LogInternal(LogLevel.Warn, string.Format(formater, args));
        }
        internal static void Warn(object msg)
        {
            LogInternal(LogLevel.Warn, msg.ToString());
        }
        internal static void Error(string formater, params object[] args)
        {
            LogInternal(LogLevel.Error, string.Format(formater, args));
        }
        internal static void Error(object msg)
        {
            LogInternal(LogLevel.Error, msg.ToString());
        }

        static void LogInternal(LogLevel level, string msg)
        {
            LogHandlerRegister.LogHandler handler = null;

            switch (level)
            {
                case LogLevel.Info:
                    handler = LogHandlerRegister.InfoHandler;
                    break;
                case LogLevel.Debug:
                    handler = LogHandlerRegister.DebugHandler;
                    break;
                case LogLevel.Warn:
                    handler = LogHandlerRegister.WarnHandler;
                    break;
                case LogLevel.Error:
                    handler = LogHandlerRegister.ErrorHandler;
                    break;
            }

            if (handler == null)
            {
                return;
            }

            handler(msg);
        }
    }
}
