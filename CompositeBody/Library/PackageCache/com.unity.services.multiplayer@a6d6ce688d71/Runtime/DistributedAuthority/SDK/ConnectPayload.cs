using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;

namespace Unity.Services.DistributedAuthority
{
    class ConnectPayload
    {
        const int KeyLength = 256;
        const byte Version = 0;
        public readonly byte[] Secret = new byte[KeyLength / 8];
        public readonly byte[] PlayerId;

        public ConnectPayload(string playerId)
        {
            PlayerId = Encoding.UTF8.GetBytes(playerId);

            var generator = RandomNumberGenerator.Create();
            generator.GetNonZeroBytes(Secret);
        }

        public string Base64SecretHash()
        {
            using var stream = new MemoryStream();
            stream.Write(Secret);
            stream.Write(PlayerId);

            var hasher = new SHA256Managed();
            var hash = hasher.ComputeHash(stream.ToArray());

            return Convert.ToBase64String(hash);
        }

        public NativeArray<byte> SerializeToNativeArray()
        {
            var size =
                sizeof(byte) // version
                + sizeof(int) // secret length
                + Secret.Length // secret
                + sizeof(int) // playerId length
                + PlayerId.Length; // playerId
            var writer = new DataStreamWriter(size, Allocator.Temp);

            // payload version
            writer.WriteByte(Version);

            // secret (length + data)
            writer.WriteInt(Secret.Length);
            writer.WriteBytes(new NativeArray<byte>(Secret, Allocator.Temp));

            // playerId (length + data)
            writer.WriteInt(PlayerId.Length);
            writer.WriteBytes(new NativeArray<byte>(PlayerId, Allocator.Temp));

            return writer.AsNativeArray();
        }
    }
}
