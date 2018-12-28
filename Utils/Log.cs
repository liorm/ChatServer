using System;

namespace Utils
{
    /// <summary>
    /// Poormans logging facility.
    /// </summary>
    public static class Log
    {
        private static readonly object LogLock = new object();

        public static void LogDebug(string msg)
        {
            lock (LogLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("DEBUG: " + msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }
        }

        public static void LogError(string msg)
        {
            lock (LogLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: " + msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }
        }

        public static void LogError(Exception e, string msg = null)
        {
            if (msg != null)
                LogError($"{msg}: {e.Message}");
            else
                LogError($"Exception '{e.GetType().Name}': {e.Message}");
        }
    }
}