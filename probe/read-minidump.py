"""Read exception code, faulting address/module and the faulting thread's stack modules from a minidump.

Usage: python -I read-minidump.py <file.dmp>
Read-only; parses only the exception, module list and memory streams.
"""
import struct
import sys

STREAM_THREAD_LIST = 3
STREAM_MODULE_LIST = 4
STREAM_EXCEPTION = 6
STREAM_MEMORY64_LIST = 9
STREAM_MEMORY_LIST = 5


def read_utf16(buf, rva):
    (length,) = struct.unpack_from("<I", buf, rva)
    return buf[rva + 4 : rva + 4 + length].decode("utf-16-le", "replace")


def main(path):
    buf = open(path, "rb").read()
    sig, _ver, nstreams, dir_rva = struct.unpack_from("<4sIII", buf, 0)
    assert sig == b"MDMP", "not a minidump"
    streams = {}
    for i in range(nstreams):
        stype, size, rva = struct.unpack_from("<III", buf, dir_rva + i * 12)
        streams[stype] = (size, rva)

    modules = []
    if STREAM_MODULE_LIST in streams:
        _, rva = streams[STREAM_MODULE_LIST]
        (count,) = struct.unpack_from("<I", buf, rva)
        for i in range(count):
            base, size = struct.unpack_from("<QI", buf, rva + 4 + i * 108)
            name_rva = struct.unpack_from("<I", buf, rva + 4 + i * 108 + 20)[0]
            modules.append((base, size, read_utf16(buf, name_rva).split("\\")[-1]))

    def where(addr):
        for base, size, name in modules:
            if base <= addr < base + size:
                return f"{name}+0x{addr - base:x}"
        return "?"

    if STREAM_EXCEPTION not in streams:
        print("no exception stream")
        return
    _, rva = streams[STREAM_EXCEPTION]
    thread_id = struct.unpack_from("<I", buf, rva)[0]
    code, flags, _rec, addr, nparams = struct.unpack_from("<IIQQI", buf, rva + 8)
    params = struct.unpack_from("<15Q", buf, rva + 8 + 32)[:nparams]
    print(f"thread {thread_id} exception 0x{code:08x} flags {flags} at 0x{addr:x} = {where(addr)}")
    if code == 0xC0000005 and nparams >= 2:
        print(f"  access violation {'write' if params[0] == 1 else 'read' if params[0] == 0 else 'exec'} at 0x{params[1]:x}")

    # Scan the faulting thread's stack memory for return addresses inside known modules.
    ctx_size, ctx_rva = struct.unpack_from("<II", buf, rva + 8 + 32 + 15 * 8 + 4)
    rsp = struct.unpack_from("<Q", buf, ctx_rva + 0x98)[0] if ctx_size >= 0xA0 else None
    rip = struct.unpack_from("<Q", buf, ctx_rva + 0xF8)[0] if ctx_size >= 0x100 else None
    print(f"  rip 0x{rip:x} = {where(rip)}" if rip else "  no context")
    if STREAM_THREAD_LIST in streams and rsp:
        _, trva = streams[STREAM_THREAD_LIST]
        (tcount,) = struct.unpack_from("<I", buf, trva)
        for i in range(tcount):
            off = trva + 4 + i * 48
            tid = struct.unpack_from("<I", buf, off)[0]
            if tid != thread_id:
                continue
            start, msize, mrva = struct.unpack_from("<QII", buf, off + 24)
            stack = buf[mrva : mrva + msize]
            begin = max(0, rsp - start)
            hits = []
            for o in range(begin, min(len(stack) - 8, begin + 0x4000), 8):
                (v,) = struct.unpack_from("<Q", stack, o)
                w = where(v)
                if w != "?":
                    hits.append(w)
            print("  stack candidates (first 40):")
            for h in hits[:40]:
                print("   ", h)


if __name__ == "__main__":
    main(sys.argv[1])
