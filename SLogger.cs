//#define NO_LOG
#define LOG_TO_FILE

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

namespace Eruptic.Core.Common.Utilities {
    /// <summary>
    /// Ultra simple logger
    /// </summary>
    /// <summary>
    /// Ultra simple logger
    /// </summary>
    public static class SLogger {
        public enum LogLevels {
            Everything = int.MinValue,
            Flood = -10000,
            Private = -1, //TODO: allow write security sensitive data to log? must be some special compiler variable
            Default = 0,
            Trace = 1,
            Warning = 10,
            Exceptions = 100,
            LogToFile = 101,
            SaveLog = 110, //Save to database
            Critical = 1000,
            None = int.MaxValue, //Log disabled            
        }
        private static LogLevels currentConsoleLogLevel = //use field here for performance reasons
#if DEBUG
        LogLevels.Default;
#else
        LogLevels.Critical;
#endif
        private static LogLevels currentFileLogLevel = LogLevels.Critical;//field for performance reasons

        public static LogLevels CurrentFileLogLevel {
            get { return currentFileLogLevel; }
            set { currentFileLogLevel = value; }
        }

        public static LogLevels CurrentConsoleLogLevel {
            get { return currentConsoleLogLevel; }
            set { currentConsoleLogLevel = value; }
        }

        private const int writeWaitTimoutMilliseconds = 20;

        /// <summary>
        /// Limit length of string to write to file. Long writes make the app unstable on mobile.
        /// </summary>
        private const int SingleMessageWriteToFileLengthLimit = 100 * 1024; //20 kb

        /// <summary>
        /// Rolling appender limit.
        /// </summary>
        private const int BufferLengthLimit = 1 * SingleMessageWriteToFileLengthLimit;

        private const int ChunkFileLengthLimit = 10 * BufferLengthLimit;

        /// <summary>
        /// Rolling appender limit.
        /// </summary>
        private const int ChunkFileCountLimit = 1000;

        /// <summary>
        /// Rolling appender limit.
        /// </summary>
        private const int LogRollLengthLimit = ChunkFileCountLimit * ChunkFileLengthLimit;

        /// <summary>
        /// Limits the length of inner exception
        /// </summary>
        private const int InnerExceptionDescriptionLenghtLimit = SingleMessageWriteToFileLengthLimit;

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

        public static void WriteConsoleLine(string messageFormat, LogLevels level = LogLevels.Default) {
            string prefix = Environment.NewLine + LogVisibilityMarkerPrefix;
            try {
                string messageFormatResult = prefix + messageFormat.Replace(Environment.NewLine, prefix);
#if DEBUG
                if (level < currentConsoleLogLevel) return;

                WriteEvent?.Invoke(null, new LogEventArgs(messageFormat));

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

        public static Task WriteCriticalAsync(Exception ex) {
            return WriteAsync(ex, LogLevels.Critical);
        }

        public static void WriteTrace(object objectToJson, LogLevels level = LogLevels.Trace,
            [CallerMemberName] string callingMethod = "",
            [CallerFilePath] string callingFilePath = "",
            [CallerLineNumber] int callingFileLineNumber = 0) {
            callingFilePath = (callingFilePath ?? "").Replace("\\", "/");
            if (objectToJson != null) {
                string jsonObject = $"object can not be serialized : {objectToJson}";
                try {
                    jsonObject = JsonConvert.SerializeObject(objectToJson, Formatting.Indented);
                } catch (Exception ex) {
                    WriteAsync(ex);
                }
                WriteTrace(jsonObject, level, callingMethod, callingFilePath, callingFileLineNumber);
            }
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
            Task.Run(async () => {
                await WriteAsync(messageFormat, level, args).ConfigureAwait(false);
            }).Wait(writeWaitTimoutMilliseconds);
#endif
        }

        public static void Write(Exception ex, LogLevels level = LogLevels.Exceptions) {
#if LOG_FAST
            WriteAsync(ex, level).ContinueWith(t => { });
#else
            Task.Run(async () => {
                await WriteAsync(ex, level).ConfigureAwait(false);
            }).Wait(writeWaitTimoutMilliseconds);
#endif
        }

        public static void Write(object parameterToConvertToJson, LogLevels level = LogLevels.Default) {
#if LOG_FAST
            WriteAsync(parameterToConvertToJson, level).ContinueWith(t => { });
#else
            Task.Run(async () => {
                await WriteAsync(parameterToConvertToJson, level).ConfigureAwait(false);
            }).Wait(writeWaitTimoutMilliseconds);
#endif
        }

        public static Task WriteAsync(Exception ex) {
            return WriteAsync(ex, LogLevels.Critical);
        }

        /// <summary>
        /// Synchronous version 
        /// </summary>
        /// <param name="messageFormat"></param>
        /// <param name="args"></param>
        public static void WriteFlood(string messageFormat, params object[] args) {
#if LOG_FAST
            WriteFloodAsync(messageFormat, args).ContinueWith(t => { });
#else
            Task.Run(async () => {
                await WriteFloodAsync(messageFormat, args).ConfigureAwait(false);
            }).Wait(writeWaitTimoutMilliseconds);
#endif
        }

        public static Task WriteFloodAsync(string messageFormat, params object[] args) {
            return WriteAsync(messageFormat, LogLevels.Flood, args);
        }

        public static Task WriteAsync(object parameterToConvertToJson, LogLevels level = LogLevels.Default) {
            try {
                var objectToWrite = JsonConvert.SerializeObject(parameterToConvertToJson, Formatting.Indented);
                return WriteAsync($"object as JSON:{Environment.NewLine} {objectToWrite}", level);
            } catch (JsonSerializationException ex) {
                return WriteAsync($"object :{Environment.NewLine} {parameterToConvertToJson}", level);
            } catch (Exception ex) {
                return WriteAsync(ex, level);
            }
        }

        public static Task WriteAsync(string messageFormat, params object[] args) {
            return WriteAsync(messageFormat, LogLevels.Default, args);
        }

        private static SemaphoreSlim logWriteAsyncLock = new SemaphoreSlim(1);
        /// <summary>
        /// Main async writing version
        /// </summary>
        /// <param name="messageFormat"></param>
        /// <param name="level"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static
#if !NO_LOG            
            async
#endif            
            Task WriteAsync(string messageFormat, LogLevels level, params object[] args) {
#if NO_LOG
            return Task.FromResult(0);
#else
            if (level < currentConsoleLogLevel && level < currentFileLogLevel)
                return;
            try {
                await logWriteAsyncLock.WaitAsync().ConfigureAwait(false);

                if (args?.Length > 0)
                    try { messageFormat = string.Format(messageFormat, args); } catch { }
#if UTC_TIME
                messageFormat = $"{DateTime.UtcNow:ddMMyy HH:mm:ss:ffffU}> {messageFormat} ";
#else
                messageFormat = $"{DateTime.Now:ddMMyy HH:mm:ss:ffff} : {messageFormat} ";
#endif

                WriteConsoleLine(messageFormat, level);
#if LOG_TO_FILE
                await LogToFile($"{messageFormat}\n", level).ConfigureAwait(false);
#endif
            } finally {
                logWriteAsyncLock.Release();
            }
#endif
        }

        /// <summary>
        /// Main async version for exception
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public static
#if !NO_LOG
            async
#endif
            Task WriteAsync(Exception exception, LogLevels level = LogLevels.Exceptions) {
            string exceptionText = string.Empty;
            try {
                exceptionText = GetExceptionDescription(exception);
#if NO_LOG
                return Task.FromResult(0);
#else
                await WriteAsync(exceptionText, level).ConfigureAwait(false);
                await FlushAsync().ConfigureAwait(false);
#endif
            } finally {
                WriteExceptionEvent?.Invoke(null, new LogEventArgs($" : {level} : \n{exceptionText}"));
            }
        }

        public static string GetExceptionDescription(Exception exception) {
            string result;
            var sb = new StringBuilder();

            sb.AppendFormat("{0}{0}************exception start***************{1}", Environment.NewLine, GetSimpleExceptionDescription(exception));
            while (exception.InnerException != null && sb.Length < InnerExceptionDescriptionLenghtLimit) {
                sb.Append(GetSimpleExceptionDescription(exception.InnerException));
                exception = exception.InnerException;
            }
            sb.AppendFormat("************exception end***************{0}{0}", Environment.NewLine);
            result = sb.ToString();
            return result;
        }

        private static string GetSimpleExceptionDescription(Exception exception) {
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

        public class LogEventArgs : EventArgs {
            public string Message { get; private set; }
            public LogEventArgs(string message) {
                Message = message;
            }
        }
        public static event EventHandler<LogEventArgs> WriteEvent;
        public static event EventHandler<LogEventArgs> WriteExceptionEvent;

        #region Logging to file

        private static bool isLoggingToFileEnabled = true;

        public static bool IsLoggingToFileEnabled {
            get { return isLoggingToFileEnabled; }
            set { isLoggingToFileEnabled = value; }
        }

#if LOG_TO_FILE
        private static readonly SemaphoreSlim logFileLock = new SemaphoreSlim(1);
        private static string rootLoggingFolder = "Logs";

        public static string RootLoggingFolder { get { return (PclRootLoggingFolder == null) ? null : PclRootLoggingFolder.Path; } }

        private static IFolder pclRootLoggingFolder;
        private static IFolder PclRootLoggingFolder {
            get {
                if (pclRootLoggingFolder == null) {
                    pclRootLoggingFolder = FileSystem.Current.LocalStorage.CreateFolderAsync(rootLoggingFolder, CreationCollisionOption.OpenIfExists).Result;
                    IsLoggingToFileEnabled &= true;
                }

                return pclRootLoggingFolder;
            }
        }

        static string logToFileBuffer = string.Empty;

        private static IFile logFile;
        private static int logFileCalculatedLength = 0;
        private static int logCunkFileCounter = ChunkFileCountLimit;

        private static async Task LogToFile(string message, LogLevels level = LogLevels.Default, bool isFlush = false) {
            if (level >= currentFileLogLevel && isLoggingToFileEnabled)
                try {
                    await logFileLock.WaitAsync().ConfigureAwait(true);
                    message = message ?? string.Empty;
                    int messageLength = message.Length;
                    message = (messageLength < SingleMessageWriteToFileLengthLimit ?
                        message : message.Substring(0, SingleMessageWriteToFileLengthLimit));

                    logToFileBuffer += message;
                    if (logToFileBuffer.Length >= BufferLengthLimit || isFlush) {
                        bool isChunkReset = logCunkFileCounter == ChunkFileCountLimit;
                        if (isChunkReset) {
                            Interlocked.Exchange(ref logCunkFileCounter, 0);
                        }

                        var isNewChunk = logFileCalculatedLength + logToFileBuffer.Length > ChunkFileLengthLimit;
                        if (isNewChunk) {
                            //var logs = await logFile.ReadAllTextAsync().ConfigureAwait(false);
                            //logs = logs.Substring(LogRollLengthLimit - logToFileBuffer.Length);
                            //await logFile.WriteAllTextAsync(logs).ConfigureAwait(true);
                            Interlocked.Increment(ref logCunkFileCounter);
                            logFile = null;
                        }

                        var sessionLogFileName = $"logid_{SessionID}_{logCunkFileCounter}.log";
                        logFile = logFile ?? await PclRootLoggingFolder.CreateFileAsync(
                                sessionLogFileName, CreationCollisionOption.OpenIfExists).ConfigureAwait(true);

                        if (isChunkReset || isNewChunk) {
                            await logFile.WriteAllTextAsync(string.Empty).ConfigureAwait(true);
                            Interlocked.Exchange(ref logFileCalculatedLength, 0);
                        }

                        await AppendToFile(logFile, logToFileBuffer).ConfigureAwait(true);
                        Interlocked.Add(ref logFileCalculatedLength, logToFileBuffer.Length);
                        logToFileBuffer = string.Empty;
                    }
                } catch (Exception ex) {
                    WriteConsoleLine(GetExceptionDescription(ex));
                } finally {
                    logFileLock.Release();
                }
        }

        public static void Flush() {
            Task.Run(async () => await FlushAsync().ConfigureAwait(false)).Wait();
        }

        public static Task FlushAsync() {
            return LogToFile(string.Empty, LogLevels.None, true);
        }

        private static async Task AppendToFile(IFile file, string message) {
            using (StreamWriter writer = new StreamWriter(
                await file.OpenAsync(FileAccess.ReadAndWrite).ConfigureAwait(true))) {
                writer.BaseStream.Seek(0, SeekOrigin.End);
                WriteConsoleLine("Writing to log file");
                await writer.WriteLineAsync(message).ConfigureAwait(true);
                await writer.FlushAsync().ConfigureAwait(true);
            }
        }
#else
        private static Task LogToFile(string message, LogLevels level = LogLevels.Default) {
            return Task.FromResult(0);
        }

        public static void Flush() {

        }
        public static string RootLoggingFolder { get { return ""; } }

        public static LogLevels CurrentFileLogLevel {
            get { return LogLevels.Default; }
            set {  }
        }
#endif

        #endregion
    }
}
