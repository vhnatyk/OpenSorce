using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if LOG_TO_FILE
using PCLStorage;
#endif
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Newtonsoft.Json;

namespace Eruptic.Common.Utilities.Logging {
    /// <summary>
    /// Ultra simple logger
    /// </summary>
    public static class SLogger {

        public enum LogLevels {
            Default = 0,
            Trace = 1,
            Warning = 10,
            Exceptions = 100,
            Critical = 1000,
            None = int.MaxValue
        }
#if DEBUG
        private const LogLevels CurrentConsoleLogLevel = LogLevels.Default;
#else
        private const LogLevels CurrentConsoleLogLevel = LogLevels.Critical;
#endif
        private const LogLevels CurrentFileLogLevel = LogLevels.Critical;

        /// <summary>
        /// Limit length of string to write to file. Long writes make the app unstable.
        /// </summary>
        private const int SingleMessageWriteToFileLengthLimit = 20 * 1024; //20 kb

        /// <summary>
        /// Rolling appender limit.
        /// </summary>
        private const int LogRollLengthLimit = 10 * 1024 * 1024;

        /// <summary>
        /// Rolling appender limit.
        /// </summary>
        private const int BufferLengthLimit = 0 * 1024;

        /// <summary>
        /// Limits the length of inner exception
        /// </summary>
        private const int InnerExceptionDescriptionLenghtLimit = 150 * 1024;//150 kb

        /// <summary>
        ///  Set with your app name, is used  in log visibility prefix. 
        /// </summary>
        private const string AppNamePrefix = "SLogger";

        /// <summary>
        /// Makes log lines more noticeable and makes filtering possible.
        /// </summary>
        private const string LogVisibilityMarkerPrefix = AppNamePrefix + " : ";


        private static readonly string sessionID = Guid.NewGuid().ToString("N");
        public static string SessionID {
            get {
                return sessionID;
            }
        }

        private static readonly SemaphoreSlim asyncLock = new SemaphoreSlim(1);

        public static void WriteConsoleLine(string messageFormat, LogLevels level = LogLevels.Default) {
            string prefix = Environment.NewLine + LogVisibilityMarkerPrefix;
            try {
                string messageFormatResult = prefix + messageFormat.Replace(Environment.NewLine, prefix);
#if DEBUG
                if (level < CurrentConsoleLogLevel) return;

                WriteEvent?.Invoke(null, new SLoggerWriteLogEventArgs(messageFormat));

                Debug.WriteLine(messageFormatResult);
#else
                //TODO: if not AppStore or AdHoc build but a Release - ??? Log to file
#endif
                messageFormatResult = null;
            } catch {
                WriteConsoleLine("SLogger error!!! problem with: " + messageFormat);
            }
        }

        public static void WriteCritical(Exception ex) {
            Write(ex, LogLevels.Critical);
        }

        public static void WriteTrace(string message = null, LogLevels level = LogLevels.Trace,
                    [CallerMemberName] string callingMethod = "",
                    [CallerFilePath] string callingFilePath = "",
                    [CallerLineNumber] int callingFileLineNumber = 0) {
            callingFilePath = (callingFilePath ?? "").Replace("\\", "/");
            Write("{0} : {1}:{2} : {3}", level, callingMethod, Path.GetFileName(callingFilePath), callingFileLineNumber, Path.GetDirectoryName(callingFilePath));
            if (message != null) Write(message, level);
        }

        public static void Write(string messageFormat, params object[] args) {
            Write(messageFormat, LogLevels.Default, args);
        }

        /// <summary>
        /// Synchronous version 
        /// </summary>
        /// <param name="messageFormat"></param>
        /// <param name="level"></param>
        /// <param name="args"></param>
        public static void Write(string messageFormat, LogLevels level, params object[] args) {
#if LOG_FAST
            WriteAsync(messageFormat, level, args).ContinueWith(t => { });
#else
            var task = Task.Run(async () => {
                await WriteAsync(messageFormat, level, args).ConfigureAwait(false);
            });
            task.Wait();
#endif
        }

        public static void Write(Exception ex, LogLevels level = LogLevels.Exceptions) {
#if LOG_FAST
            WriteAsync(ex, level).ContinueWith(t => { });
#else
            var task = Task.Run(async () => {
                await WriteAsync(ex, level).ConfigureAwait(false);
            });
            task.Wait();
#endif
        }

        public static void Write(object parameterToConvertToJson, LogLevels level = LogLevels.Default) {
#if LOG_FAST
            WriteAsync(parameterToConvertToJson, level).ContinueWith(t => { });
#else
            var task = Task.Run(async () => {
                await WriteAsync(parameterToConvertToJson, level).ConfigureAwait(false);
            });
            task.Wait();
#endif
        }

        public static Task WriteCriticalAsync(Exception ex) {
            return WriteAsync(ex, LogLevels.Critical);
        }

        public static Task WriteAsync(object parameterToConvertToJson, LogLevels level = LogLevels.Default) {
            return WriteAsync($"object as JSON:{Environment.NewLine} {JsonConvert.SerializeObject(parameterToConvertToJson, Formatting.Indented)}", level);
        }

        public static Task WriteAsync(string messageFormat, params object[] args) {
            return WriteAsync(messageFormat, LogLevels.Default, args);
        }

#pragma warning disable CS1998 // This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
        /// <summary>
        /// Main async writing version
        /// </summary>
        /// <param name="messageFormat"></param>
        /// <param name="level"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static async Task WriteAsync(string messageFormat, LogLevels level, params object[] args) {
#pragma warning restore CS1998 // This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.

#if !NO_LOG
#if LOCK_LOGCONSOLE
            lock (lockFlag)
            {
#endif
            {
                if (level < CurrentConsoleLogLevel && level < CurrentFileLogLevel) return;
                string message = messageFormat;

                if (args != null && args.Length > 0) {
                    try {
                        message = string.Format(messageFormat, args);
                        message = string.Format("{0:ddMMyy:hh:mm:ss:ffff} : {1} ", DateTime.Now, message);
                    } catch { }
                }

                WriteConsoleLine(message, level);
                await LogToFile(message, level).ConfigureAwait(false);
            }
#if LOCK_LOGCONSOLE
            }
#endif
#endif
        }

        /// <summary>
        /// Main async version for exception
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public static Task WriteAsync(Exception exception, LogLevels level = LogLevels.Exceptions) {
            Task t;
            string exceptionText = string.Empty;
#if !NO_LOG
            var sb = new StringBuilder();

            sb.AppendFormat("{0}{0}************exception start***************{1}", Environment.NewLine, GetExceptionDescription(exception));
            while (exception.InnerException != null && sb.Length < InnerExceptionDescriptionLenghtLimit) {
                sb.Append(GetExceptionDescription(exception.InnerException));
                exception = exception.InnerException;
            }
            sb.AppendFormat("************exception end***************{0}{0}", Environment.NewLine);
            exceptionText = sb.ToString();
            t = WriteAsync(exceptionText, level);
#else
            t = Task.FromResult(0);
#endif
            WriteExceptionEvent?.Invoke(null, new SLoggerWriteLogEventArgs($" : {level} : \n{exceptionText}"));
            return t;
        }

        public static string GetExceptionDescription(Exception exception) {
            StringBuilder sb = new StringBuilder();
            const string stackEntrySeparator = "\t at ";
            //sb.AppendLine("**************************************************************************************");
            sb.AppendLine();
            sb.AppendLine("Message: ");
            sb.AppendLine(exception.Message);
            sb.AppendLine("**************************************************************************************");
            sb.AppendLine("Stack trace: ");
            sb.AppendLine((exception.StackTrace ?? "").Replace(" at ", stackEntrySeparator));
            sb.AppendLine("**************************************************************************************");
            sb.AppendLine("Source: ");
            sb.AppendLine((exception.Source ?? "").Replace(" at ", stackEntrySeparator));
            sb.AppendLine();
            //sb.AppendLine("**************************************************************************************\n");

            return sb.ToString();
        }

        public static string GetDescription(this Exception exception) {
            return GetExceptionDescription(exception);
        }

        public class SLoggerWriteLogEventArgs : EventArgs {
            public string Message { get; private set; }
            public SLoggerWriteLogEventArgs(string message) {
                Message = message;
            }
        }
        public static event EventHandler<SLoggerWriteLogEventArgs> WriteEvent;
        public static event EventHandler<SLoggerWriteLogEventArgs> WriteExceptionEvent;

        #region Logging to file

        private static bool isLoggingToFileEnabled = true;

        public static bool IsLoggingToFileEnabled {
            get { return isLoggingToFileEnabled; }
            set { isLoggingToFileEnabled = value; }
        }

#if LOG_TO_FILE

        private static string rootLoggingFolder = "Logs";

        public static string RootLoggingFolder { get { return (PclRootLoggingFolder == null) ? null : PclRootLoggingFolder.Path; } }

        private static IFolder pclRootLoggingFolder;
        private static IFolder PclRootLoggingFolder
        {
            get
            {
                if (pclRootLoggingFolder == null)
                {
                    pclRootLoggingFolder = FileSystem.Current.LocalStorage.CreateFolderAsync(rootLoggingFolder, CreationCollisionOption.OpenIfExists).Result;
                    IsLoggingToFileEnabled &= true;
                }

                return pclRootLoggingFolder;
            }
        }

        static string logToFileBuffer = string.Empty;

        private static async Task LogToFile(string message, LogLevels level = CurrentFileLogLevel, bool isFlush = false)
        {
            if(level < CurrentFileLogLevel) return;
            try
            {
                if (IsLoggingToFileEnabled)
                {
                    await asyncLock.WaitAsync();
                    WriteConsoleLine("Writing to log file");
                    message = message ?? string.Empty;
                    int messageLength = message.Length;
                    message = (messageLength < SingleMessageWriteToFileLengthLimit ?
                        message : message.Substring(0, SingleMessageWriteToFileLengthLimit));
                    string sessionLogFileName = String.Format("logid_{0}.log", SessionID);

                    IFile logFile = await PclRootLoggingFolder.CreateFileAsync(sessionLogFileName, CreationCollisionOption.OpenIfExists);
                    var logs = await logFile.ReadAllTextAsync();

                    message = string.Format("{0:dd-MM-yy HH:mm:ss} {1}", DateTime.Now, message);

                    if (!string.IsNullOrWhiteSpace(logs) && logs.Length > LogRollLengthLimit)//TODO: improve performance with chunks
                    {
                        logs = logs.Substring(LogRollLengthLimit - message.Length);
                        await logFile.WriteAllTextAsync(logs);
                    }
                    if (logToFileBuffer.Length < BufferLengthLimit && !isFlush)
                    {
                        logToFileBuffer += message;
                    }
                    else
                    {
                        await AppendToFile(logFile, logToFileBuffer.Length > 0 ? logToFileBuffer : message);
                        logToFileBuffer = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteConsoleLine(GetExceptionDescription(ex));
            }
            finally
            {
                asyncLock.Release();
            }
        }

        public static void Flush()
        {
            Task.Run(async () => await LogToFile(string.Empty, LogLevels.Critical, true));
        }

        private static async Task AppendToFile(IFile file, string message)
        {
            using (StreamWriter writer = new StreamWriter(await file.OpenAsync(FileAccess.ReadAndWrite)))
            {
                writer.BaseStream.Seek(0, SeekOrigin.End);
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();
            }
        }
#else
        private static Task LogToFile(string message, LogLevels level = LogLevels.Default) {
            return Task.FromResult(0);
        }

        public static void Flush() {

        }
        public static string RootLoggingFolder { get { return ""; } }
#endif

        #endregion
    }
}