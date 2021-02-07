using ICSharpCode.SharpZipLib.Zip.Compression;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace arkitSharp
{
    /// <summary>
    /// 
    /// Based on Arkit by James E
    /// Reference:  https://github.com/project-umbrella/arkit.py/blob/master/arkit.py
    /// </summary>
    class Program
    {
        private static readonly long ValidSigVer = 2653586369;
        static void Main(string[] args)
        {
            Serilog.Events.LogEventLevel logLevel;

            string logLevelSelected = args.FirstOrDefault(x => x.StartsWith("log=")) ?? "log=fatal";
            switch (logLevelSelected.Split('=')[1].ToLower())
            {
                case "debug":
                    logLevel = Serilog.Events.LogEventLevel.Debug;
                    break;
                case "warning":
                    logLevel = Serilog.Events.LogEventLevel.Warning;
                    break;
                case "information":
                    logLevel = Serilog.Events.LogEventLevel.Information;
                    break;
                case "verbose":
                    logLevel = Serilog.Events.LogEventLevel.Verbose;
                    break;
                case "fatal":
                default:
                    logLevel = Serilog.Events.LogEventLevel.Fatal;
                    break;
            }
            
            Log.Logger = new LoggerConfiguration()
                .WriteTo
                .Console(logLevel, theme: AnsiConsoleTheme.Code)
                .CreateLogger();

            bool wasSuccessful = Unpack(args[0], args[1], Log.Logger);
            if (wasSuccessful)
            {
                return;
            }

            else Log.Logger.Error("Unknown error, please set log level to debug using log=debug");

            // failed, cleanup file if created.

        }

        public static bool Unpack(string source, string dest, ILogger log)
        {
            bool IsSuccessful = false;

            using(var freader = File.OpenRead(source))
            {
                long sigver = Get8byteChunk(freader);

                if (sigver != ValidSigVer)
                {
                    log.Error($"Error: Invalid signature format. Expected: {ValidSigVer} Found: {sigver}");
                    return IsSuccessful;
                }

                //size_unpacked_chunk, the unpacked chunk size
                long unpackedChunkSize = Get8byteChunk(freader);
                // size_packed, the size of the file fully unpacked
                long packedTotalSize = Get8byteChunk(freader);
                //size_unpacked, the size of the file when unpacked.
                long unpackedTotalSize = Get8byteChunk(freader);

                long sizeIndexed = 0;

                var compressionIndex = new List<(long compressedSize, long uncompressedSize)>();
                while(sizeIndexed < unpackedTotalSize)
                {
                    long compressed = Get8byteChunk(freader);
                    long uncompressed = Get8byteChunk(freader);
                    compressionIndex.Add((compressed, uncompressed));
                    sizeIndexed += uncompressed;
                    Log.Debug($"Compression Index Size: {compressionIndex.Count} Size Indexed: {sizeIndexed} Size Unpacked: {unpackedTotalSize} Compressed Length: {compressed} Uncompressed: {uncompressed}");                 
                }

                if(sizeIndexed != unpackedTotalSize)
                {
                    Log.Error($"Header-Index mismatch. Expected Uncompressed Bytes: {unpackedTotalSize} Actual: {sizeIndexed}.");
                    return IsSuccessful;
                }

                using(var fwriter = File.OpenWrite(dest))
                {
                    foreach(var data in compressionIndex)
                    {
                        byte[] buffer = new byte[data.compressedSize];
                        int bytesRead = freader.Read(buffer);
                        var decompressedData = DecompressZlib(buffer, data.uncompressedSize);

                        if (decompressedData.Length != data.uncompressedSize)
                        {
                            Log.Error($"Chunk size mismatch. Expected: {data.uncompressedSize} Actual: {bytesRead}");                 
                            return IsSuccessful;
                        }

                        fwriter.Write(decompressedData);
                        fwriter.Flush();
                    }
                }

                IsSuccessful = true;
            }

            return IsSuccessful;
        }

        private static long Get8byteChunk(FileStream freader)
        {
            byte[] buffer = new byte[8];
            int readBytes = freader.Read(buffer);

            return BitConverter.ToInt64(buffer);
        }

        private static byte[] DecompressZlib(byte[] compressedData, long uncompressedSize)
        {
            byte[] outputBuffer = new byte[uncompressedSize];
            var inf = new Inflater();
            inf.SetInput(compressedData);
            inf.Inflate(outputBuffer);
            return outputBuffer;
        }
    }
}
