"""Print the OSC that TouchDesignerSender is sending, so Unity can be ruled in or out
before anything is wired up in TouchDesigner.

Run it INSTEAD of TD (both cannot hold the same UDP port):

    py tools/osc_listen.py
    py tools/osc_listen.py --port 10000 --once

What it tells you, in order of how often it is the answer:

  nothing at all      Unity is not sending. Check the sender is enabled, that you are
                      the host (m_HostOnly defaults on), and that the port matches.
  packets, good names Unity is fine. The problem is on the TD side -- the OSC In CHOP's
                      port, or it is not cooking.
  a decode error      The packet is malformed, which is a bug in OscWriter, not config.

Stdlib only, because the point is to have no reason this itself does not run.
"""

import argparse
import socket
import struct
import sys
import time


def read_padded_string(buf, i):
    """OSC strings are null-terminated and then padded with nulls to a 4-byte boundary."""
    end = buf.index(b"\x00", i)
    text = buf[i:end].decode("ascii", "replace")
    j = end + 1
    while j % 4 != 0:
        j += 1
    return text, j


def read_argument(tag, buf, i):
    """One argument of the type named by a type-tag character. OSC is big-endian."""
    if tag == "f":
        return struct.unpack_from(">f", buf, i)[0], i + 4
    if tag == "i":
        return struct.unpack_from(">i", buf, i)[0], i + 4
    if tag == "s":
        return read_padded_string(buf, i)
    if tag in "TF":
        return tag == "T", i          # booleans live in the tag, not the body
    raise ValueError(f"unsupported OSC type tag {tag!r}")


def decode_message(buf):
    address, i = read_padded_string(buf, 0)
    if i >= len(buf):
        return [(address, [])]
    tags, i = read_padded_string(buf, i)
    values = []
    for tag in tags.lstrip(","):
        value, i = read_argument(tag, buf, i)
        values.append(value)
    return [(address, values)]


def decode(buf):
    """A bundle or a bare message, flattened to [(address, [values])]. Bundles nest."""
    if buf.startswith(b"#bundle\x00"):
        out = []
        i = 16                                     # 8 header + 8 time tag
        while i + 4 <= len(buf):
            (size,) = struct.unpack_from(">i", buf, i)
            i += 4
            if size < 0 or i + size > len(buf):
                raise ValueError(f"element size {size} runs past the end of the datagram")
            out.extend(decode(buf[i:i + size]))
            i += size
        return out
    return decode_message(buf)


def fmt(value):
    return f"{value:>9.3f}" if isinstance(value, float) else f"{value!s:>9}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=10000)
    ap.add_argument("--host", default="0.0.0.0",
                    help="0.0.0.0 listens on every interface, which is what you want when "
                         "Unity is on another machine.")
    ap.add_argument("--once", action="store_true", help="Print one packet in full, then exit.")
    ap.add_argument("--hz", type=float, default=2.0, help="Lines per second when streaming.")
    args = ap.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind((args.host, args.port))
    except OSError as e:
        print(f"cannot listen on {args.host}:{args.port} -- {e}", file=sys.stderr)
        print("If TouchDesigner is open, its OSC In CHOP already holds this port. "
              "Close it, or point one of the two at a different port.", file=sys.stderr)
        return 1

    sock.settimeout(3.0)
    print(f"listening on {args.host}:{args.port}  (ctrl-c to stop)\n")

    packets = 0
    first = None
    last_print = 0.0

    while True:
        try:
            data, sender = sock.recvfrom(65535)
        except socket.timeout:
            if packets == 0:
                print("nothing in 3 s. Unity is not sending to this port -- check the sender "
                      "is enabled, that this machine is the host, and that the ports match.")
            continue
        except KeyboardInterrupt:
            break

        packets += 1
        if first is None:
            first = time.time()

        try:
            messages = decode(data)
        except Exception as e:
            print(f"MALFORMED packet, {len(data)} bytes from {sender[0]}: {e}")
            continue

        if args.once:
            print(f"{len(data)} bytes from {sender[0]}, {len(messages)} messages:\n")
            width = max(len(a) for a, _ in messages)
            for address, values in messages:
                print(f"  {address.ljust(width)}  {' '.join(fmt(v) for v in values)}")
            elapsed = max(time.time() - first, 1e-6)
            print(f"\nlooks well formed. In TD: an OSC In CHOP on port {args.port} will "
                  f"expose these as channels.")
            return 0

        now = time.time()
        if now - last_print < 1.0 / max(args.hz, 0.1):
            continue
        last_print = now

        values = dict(messages)
        show = values.get("/cb/show/time", [0.0])[0]
        held = values.get("/cb/show/held", [0.0])[0]
        head_a = values.get("/cb/a/head/pos", [0.0, 0.0, 0.0])
        head_b = values.get("/cb/b/head/pos", [0.0, 0.0, 0.0])
        rate = packets / max(now - first, 1e-6)

        print(f"{rate:5.1f} Hz  {len(data):4d} B  "
              f"show {show:8.2f}{'  HELD' if held else '      '}  "
              f"A {' '.join(f'{v:6.2f}' for v in head_a)}  "
              f"B {' '.join(f'{v:6.2f}' for v in head_b)}")

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print()
