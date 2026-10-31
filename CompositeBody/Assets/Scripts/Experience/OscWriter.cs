using System.Runtime.InteropServices;
using System.Text;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Writes OSC packets into a byte buffer you already own. No allocation, no library.
    ///
    /// WHY HAND-ROLLED. Every OSC package for Unity allocates -- a byte[] per message, a string
    /// per address, a List per bundle. At 60 Hz with thirty-odd values that is a steady drip of
    /// garbage, and a collection landing inside an 11 ms frame budget is a visible hitch in a
    /// headset. OSC's wire format is four rules (4-byte alignment, null-terminated padded
    /// strings, a comma-led type tag string, big-endian arguments), so writing it costs less than
    /// importing something that gets it subtly wrong.
    ///
    /// WHY BIG-ENDIAN BY HAND. OSC is network byte order and x86 is not, so every float has to be
    /// byte-swapped. The explicit-layout struct below does that without `unsafe` and without
    /// BitConverter.GetBytes, which would allocate a four-byte array per float.
    ///
    /// Overflow is tracked rather than thrown: a truncated OSC packet is worse than no packet,
    /// because the receiver reads a length prefix that then runs off the end of the datagram.
    /// Check <see cref="overflowed"/> before sending.
    /// </summary>
    public struct OscWriter
    {
        [StructLayout(LayoutKind.Explicit)]
        struct FloatBits
        {
            [FieldOffset(0)] public float asFloat;
            [FieldOffset(0)] public int asInt;
        }

        // Spelled out as bytes rather than as an escaped string literal so the trailing null is
        // visible: "#bundle" plus its terminator is exactly eight bytes, which is why the bundle
        // header needs no padding. Static because BeginBundle runs once per packet and building
        // this from a string would allocate sixty times a second for a constant.
        static readonly byte[] k_BundleHeader =
        {
            (byte)'#', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e', 0
        };

        readonly byte[] m_Buffer;
        int m_Head;
        int m_OpenSizeAt;
        bool m_Overflowed;

        public OscWriter(byte[] buffer)
        {
            m_Buffer = buffer;
            m_Head = 0;
            m_OpenSizeAt = -1;
            m_Overflowed = false;
        }

        /// <summary>Bytes written so far. Pass this as the datagram length.</summary>
        public int length => m_Head;

        /// <summary>True if the buffer ran out. The contents are then not a valid packet.</summary>
        public bool overflowed => m_Overflowed;

        /// <summary>
        /// Opens a bundle, so everything written after it arrives in one datagram and therefore
        /// at one instant. That atomicity is the reason to bundle at all: head position in one
        /// packet and show time in the next lets the receiver cook a frame that mixes a pose from
        /// now with a time from a tick ago.
        /// </summary>
        public void BeginBundle()
        {
            WriteRaw(k_BundleHeader);

            // Time tag, as two 32-bit halves. The spec reserves the value 1 for "play this
            // immediately", which is what a live stream wants -- anything else asks the receiver
            // to schedule it against a clock we are not synchronised to.
            WriteInt(0);
            WriteInt(1);
        }

        public void Message(string address, float value)
        {
            Message(address, null, value);
        }

        /// <summary>
        /// The address in two pieces, joined on the wire. Lets a caller keep per-player prefixes
        /// and per-value suffixes as literals instead of concatenating a string per value per
        /// frame, which is the allocation this whole struct exists to avoid.
        /// </summary>
        public void Message(string addressA, string addressB, float value)
        {
            OpenElement();
            WriteString(addressA, addressB);
            WriteString(",f", null);
            WriteFloat(value);
            CloseElement();
        }

        public void Message(string addressA, string addressB, float x, float y, float z)
        {
            OpenElement();
            WriteString(addressA, addressB);
            WriteString(",fff", null);
            WriteFloat(x);
            WriteFloat(y);
            WriteFloat(z);
            CloseElement();
        }

        /// <summary>
        /// A string argument, which goes on the wire exactly like an address: terminated and
        /// padded to a 4-byte boundary.
        ///
        /// TouchDesigner's OSC In CHOP only makes channels out of numbers, so a string reaches an
        /// OSC In DAT instead -- and a DAT appends a row per message, which is why a caller should
        /// send strings on a change rather than once a frame. ASCII, like every other string here:
        /// a CJK label would arrive as question marks, so the codes sent over this are ASCII by
        /// design.
        /// </summary>
        public void Message(string addressA, string addressB, string value)
        {
            OpenElement();
            WriteString(addressA, addressB);
            WriteString(",s", null);
            WriteString(value ?? string.Empty, null);
            CloseElement();
        }

        // --- bundle element framing -------------------------------------------------------------
        // Each element in a bundle is preceded by its own size, which is only known once it has
        // been written. OpenElement leaves a hole; CloseElement patches it.

        void OpenElement()
        {
            m_OpenSizeAt = m_Head;
            WriteInt(0);
        }

        void CloseElement()
        {
            if (m_Overflowed || m_OpenSizeAt < 0) return;

            int size = m_Head - m_OpenSizeAt - 4;
            m_Buffer[m_OpenSizeAt] = (byte)(size >> 24);
            m_Buffer[m_OpenSizeAt + 1] = (byte)(size >> 16);
            m_Buffer[m_OpenSizeAt + 2] = (byte)(size >> 8);
            m_Buffer[m_OpenSizeAt + 3] = (byte)size;
            m_OpenSizeAt = -1;
        }

        // --- primitives -------------------------------------------------------------------------

        /// <summary>
        /// One or two segments, then a null terminator, then nulls out to the next 4-byte
        /// boundary. A string whose length is already a multiple of four still gets four nulls:
        /// the terminator is not optional, so the pad becomes a full word.
        /// </summary>
        void WriteString(string a, string b)
        {
            int needed = a.Length + (b?.Length ?? 0);
            needed += 4 - (needed & 3);
            if (!Fits(needed)) return;

            m_Head += Encoding.ASCII.GetBytes(a, 0, a.Length, m_Buffer, m_Head);
            if (b != null) m_Head += Encoding.ASCII.GetBytes(b, 0, b.Length, m_Buffer, m_Head);

            m_Buffer[m_Head++] = 0;
            while ((m_Head & 3) != 0) m_Buffer[m_Head++] = 0;
        }

        void WriteFloat(float value)
        {
            FloatBits bits = default;
            bits.asFloat = value;
            WriteInt(bits.asInt);
        }

        void WriteInt(int value)
        {
            if (!Fits(4)) return;
            m_Buffer[m_Head++] = (byte)(value >> 24);
            m_Buffer[m_Head++] = (byte)(value >> 16);
            m_Buffer[m_Head++] = (byte)(value >> 8);
            m_Buffer[m_Head++] = (byte)value;
        }

        void WriteRaw(byte[] bytes)
        {
            if (!Fits(bytes.Length)) return;
            System.Buffer.BlockCopy(bytes, 0, m_Buffer, m_Head, bytes.Length);
            m_Head += bytes.Length;
        }

        bool Fits(int bytes)
        {
            if (m_Overflowed) return false;
            if (m_Head + bytes > m_Buffer.Length)
            {
                m_Overflowed = true;
                return false;
            }
            return true;
        }
    }
}
