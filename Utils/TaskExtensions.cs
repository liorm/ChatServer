using System;
using System.Threading.Tasks;

namespace Utils
{
    public static class TaskExtensions
    {
        public static void Forget(this Task task, bool rethrow = true)
        {
            task.Forget(e =>
            {
                Log.LogError(e, "Unhandled task exception");
                if (rethrow)
                    throw new AggregateException(e);
            });
        }

        public static void Forget(this Task task, Action<Exception> onErrorCallback)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    onErrorCallback(t.Exception);
            });
        }
    }
}