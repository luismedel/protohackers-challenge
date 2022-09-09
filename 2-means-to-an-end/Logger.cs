using System.Diagnostics;
using System.Net.Sockets;

namespace Protohackers
{
    internal class Logger
    {
        public static void AddTraceListener (TraceListener listener) => Trace.Listeners.Add (listener);

        public static void Indent () => Trace.Indent ();
        public static void Unindent () => Trace.Unindent ();

        public static void Info (string format, params object[] args) => Trace.WriteLine (Format ("INFO", format, args));
        public static void Warn (string format, params object[] args) => Trace.WriteLine (Format ("WARN", format, args));
        public static void Error (string format, params object[] args) => Trace.WriteLine (Format ("ERROR", format, args));
        public static void Exception (Exception ex)
        {
            if (ex is SocketException sockEx)
                Trace.WriteLine (Format ("ERROR", $"{sockEx.Message} (code: {sockEx.ErrorCode})"));
            else
                Trace.WriteLine (Format ("ERROR", ex.Message));
        }

        [Conditional ("DEBUG")]
        public static void Debug (string format, params object[] args) => System.Diagnostics.Debug.WriteLine (Format ("DEBUG", format, args));

        static string Format (string level, string format, params object[] args)
        {
            if (args.Length == 0)
                return $"[{DateTime.Now.ToString ("s")}] [{level:5}] {format}";
            else
                return string.Format ($"[{DateTime.Now.ToString ("s")}] [{level:5}] {format}", args);
        }
    }
}
