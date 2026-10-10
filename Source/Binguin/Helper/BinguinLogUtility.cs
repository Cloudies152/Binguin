namespace Binguin.Helper
{
    /// <summary>统一日志入口：0=消息，1=警告，2=错误。调试日志仅在 Debug 构建输出。</summary>
    public static class BinguinLogUtility
    {
        private const string Prefix = "[冰鹅族] ";

        public static void Log(string message, int severity = 0, bool isDebug = true)
        {
            if (!ShouldLog(isDebug)) return;
            DispatchLog(message, severity);
        }

        public static void WarningOnce(string message, int key, bool isDebug = false)
        {
            if (!ShouldLog(isDebug)) return;
            Verse.Log.WarningOnce(Prefix + message, key);
        }

        private static bool ShouldLog(bool isDebug)
        {
#if DEBUG
            return true;
#else
            return !isDebug;
#endif
        }

        private static void DispatchLog(string message, int severity)
        {
            message = Prefix + message;
            switch (severity)
            {
                case 0:
                    Verse.Log.Message(message);
                    break;
                case 1:
                    Verse.Log.Warning(message);
                    break;
                case 2:
                    Verse.Log.Error(message);
                    break;
                default:
                    Verse.Log.Message(message);
                    break;
            }
        }
    }
}
