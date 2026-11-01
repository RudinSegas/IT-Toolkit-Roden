using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Toolkit.Core
{
    [DataContract]
    public sealed class ProbeMessage
    {
        [DataMember] public int Version;
        [DataMember] public string Type;
        [DataMember] public string Session;
        [DataMember] public string Request;
        [DataMember] public string Nonce;
        [DataMember] public string Operation;
        [DataMember] public bool Elevated;
        [DataMember] public int ProcessId;
        [DataMember] public string Text;
        [DataMember] public string Channel;
        [DataMember] public double? Percent;
        [DataMember] public int? ExitCode;
        [DataMember] public long Sequence;
        [DataMember] public string Verdict;
        [DataMember] public int? NativeExitCode;
        public void Validate(string type, string session, string request, string nonce)
        {
            if (Version != 1 || Type != type || Session != session || Request != request || Nonce != nonce || !Operations.IsElevated(Operation))
                throw new InvalidDataException("Rejected worker protocol message.");
            if (!Regex.IsMatch(Session ?? "", "\\A[0-9a-f]{32}\\z") || !Regex.IsMatch(Request ?? "", "\\A[0-9a-f]{32}\\z") || !Regex.IsMatch(Nonce ?? "", "\\A[0-9a-f]{64}\\z"))
                throw new InvalidDataException("Invalid session binding.");
            if(Verdict!=null&&!Enum.IsDefined(typeof(ChangeVerdict),Verdict))throw new InvalidDataException("Invalid verdict.");
            if ((Text ?? "").Length > 2048 || (Channel != null && Channel != "stdout" && Channel != "stderr" && Channel != "status")) throw new InvalidDataException("Invalid output schema.");
            if (Percent.HasValue && (Double.IsNaN(Percent.Value) || Double.IsInfinity(Percent.Value) || Percent < 0 || Percent > 100)) throw new InvalidDataException("Invalid progress.");
        }
    }
    public static class ProbeProtocol
    {
        public const int MaximumFrame = 16384;
        public static async Task WriteAsync(Stream stream, ProbeMessage message, CancellationToken token)
        {
            byte[] body;
            using (var m = new MemoryStream()) { new DataContractJsonSerializer(typeof(ProbeMessage)).WriteObject(m, message); body = m.ToArray(); }
            if (body.Length == 0 || body.Length > MaximumFrame) throw new InvalidDataException("Oversized frame.");
            var size = BitConverter.GetBytes(body.Length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(size);
            await stream.WriteAsync(size, 0, size.Length, token).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        private static async Task ReadExactly(Stream stream, byte[] data, CancellationToken token)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int n = await stream.ReadAsync(data, offset, data.Length - offset, token).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("Worker channel ended before a complete frame.");
                offset += n;
            }
        }
        public static async Task<ProbeMessage> ReadAsync(Stream stream, CancellationToken token)
        {
            var header = new byte[4]; await ReadExactly(stream, header, token).ConfigureAwait(false);
            if (!BitConverter.IsLittleEndian) Array.Reverse(header);
            int size = BitConverter.ToInt32(header, 0);
            if (size < 1 || size > MaximumFrame) throw new InvalidDataException("Invalid worker frame length.");
            var body = new byte[size]; await ReadExactly(stream, body, token).ConfigureAwait(false);
            using (var memory = new MemoryStream(body))
            {
                var message = (ProbeMessage)new DataContractJsonSerializer(typeof(ProbeMessage), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 32 }).ReadObject(memory);
                if (message == null) throw new InvalidDataException("Empty message.");
                return message;
            }
        }
    }
}
