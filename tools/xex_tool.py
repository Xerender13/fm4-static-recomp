"""Inspect a retail XEX2: unpack its image and disassemble PowerPC code.

    xex_tool.py info  <xex>
    xex_tool.py dis   <xex> <address> [count]
    xex_tool.py func  <xex> <address>          # PDATA entry covering the address
    xex_tool.py xrefs <xex> <address>          # branches and data pointers to it
    xex_tool.py bytes <xex> <address> [size]
    xex_tool.py dump  <xex> <out.bin>
"""
import argparse
import bisect
import struct
import sys

from capstone import CS_ARCH_PPC, CS_MODE_32, CS_MODE_BIG_ENDIAN, Cs
from Crypto.Cipher import AES

RETAIL_KEY = bytes.fromhex("20B185A59D28FDC340583FBB0896BF91")
HDR_FILE_FORMAT = 0x000003FF
HDR_ENTRY_POINT = 0x00010100
HDR_IMAGE_BASE = 0x00010201


class Xex:
    def __init__(self, path):
        raw = open(path, "rb").read()
        if raw[:4] != b"XEX2":
            raise SystemExit("not an XEX2 file")
        _, pe_offset, _, sec_offset, count = struct.unpack(">IIIII", raw[4:24])
        headers = dict(struct.unpack(">II", raw[24 + i * 8:32 + i * 8]) for i in range(count))
        self.entry_point = headers[HDR_ENTRY_POINT]
        self.base = headers[HDR_IMAGE_BASE]
        image_size = struct.unpack(">I", raw[sec_offset + 4:sec_offset + 8])[0]
        file_key = raw[sec_offset + 0x150:sec_offset + 0x160]

        fmt = headers[HDR_FILE_FORMAT]
        info_size, encryption, compression = struct.unpack(">IHH", raw[fmt:fmt + 8])
        if compression != 1:
            raise SystemExit(f"unsupported compression type {compression}")
        payload = raw[pe_offset:]
        if encryption:
            session_key = AES.new(RETAIL_KEY, AES.MODE_ECB).decrypt(file_key)
            payload = payload[:len(payload) & ~15]
            payload = AES.new(session_key, AES.MODE_CBC, iv=bytes(16)).decrypt(payload)

        image = bytearray()
        pos = 0
        for i in range((info_size - 8) // 8):
            data_size, zero_size = struct.unpack(">II", raw[fmt + 8 + i * 8:fmt + 16 + i * 8])
            image += payload[pos:pos + data_size]
            image += bytes(zero_size)
            pos += data_size
        self.image = bytes(image.ljust(image_size, b"\0"))
        if self.image[:2] != b"MZ":
            raise SystemExit("unpacked image has no MZ header (wrong key?)")
        self._parse_pe()

    def _parse_pe(self):
        img = self.image
        pe = struct.unpack("<I", img[0x3C:0x40])[0]
        nsections, = struct.unpack("<H", img[pe + 6:pe + 8])
        opt_size, = struct.unpack("<H", img[pe + 20:pe + 22])
        opt = pe + 24
        pdata_rva, pdata_size = struct.unpack("<II", img[opt + 96 + 3 * 8:opt + 96 + 4 * 8])
        self.sections = []
        table = opt + opt_size
        for i in range(nsections):
            s = img[table + i * 40:table + (i + 1) * 40]
            name = s[:8].rstrip(b"\0").decode("latin-1")
            vsize, rva = struct.unpack("<II", s[8:16])
            flags, = struct.unpack("<I", s[36:40])
            self.sections.append((name, self.base + rva, vsize, flags))
        self.pdata = []
        for off in range(pdata_rva, pdata_rva + pdata_size, 8):
            begin, packed = struct.unpack(">II", img[off:off + 8])
            if begin:
                self.pdata.append((begin, ((packed >> 8) & 0x3FFFFF) * 4))
        self.pdata.sort()

    def read(self, address, size):
        off = address - self.base
        return self.image[off:off + size]

    def function_at(self, address):
        i = bisect.bisect_right(self.pdata, (address, 0xFFFFFFFF)) - 1
        if i >= 0:
            begin, size = self.pdata[i]
            if begin <= address < begin + size:
                return begin, size
        return None

    def branch_xrefs(self, target):
        for name, va, size, flags in self.sections:
            if not flags & 0x20:
                continue
            for a in range(va, va + size - 3, 4):
                word = int.from_bytes(self.read(a, 4), "big")
                op = word >> 26
                if op == 18 and not word & 2:
                    dest = a + ((word & 0x03FFFFFC) ^ 0x02000000) - 0x02000000
                elif op == 16 and not word & 2:
                    dest = a + ((word & 0xFFFC) ^ 0x8000) - 0x8000
                else:
                    continue
                if dest == target:
                    yield a

    def disassemble(self, address, count):
        md = Cs(CS_ARCH_PPC, CS_MODE_32 | CS_MODE_BIG_ENDIAN)
        for i in range(count):
            a = address + i * 4
            word = self.read(a, 4)
            if len(word) < 4:
                break
            owner = self.function_at(a)
            mark = "|" if owner else " "
            if owner and owner[0] == a:
                mark = ">"
            ins = next(md.disasm(word, a), None)
            text = f"{ins.mnemonic} {ins.op_str}" if ins else f".long 0x{word.hex()}"
            yield f"{a:08X} {mark} {word.hex()}  {text}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("command", choices=["info", "dis", "func", "xrefs", "bytes", "dump"])
    ap.add_argument("xex")
    ap.add_argument("args", nargs="*")
    a = ap.parse_args()
    xex = Xex(a.xex)
    if a.command == "info":
        print(f"base={xex.base:08X} entry={xex.entry_point:08X} image_size={len(xex.image):#x} "
              f"pdata_functions={len(xex.pdata)}")
        for name, va, size, flags in xex.sections:
            print(f"  {name:<8} {va:08X}-{va + size:08X} flags={flags:08X}")
    elif a.command == "dis":
        address = int(a.args[0], 16)
        count = int(a.args[1]) if len(a.args) > 1 else 16
        print("\n".join(xex.disassemble(address, count)))
    elif a.command == "func":
        address = int(a.args[0], 16)
        owner = xex.function_at(address)
        if owner:
            print(f"{owner[0]:08X} size={owner[1]:#x} end={owner[0] + owner[1]:08X}")
        else:
            i = bisect.bisect_right(xex.pdata, (address, 0xFFFFFFFF))
            prev = xex.pdata[i - 1] if i else None
            nxt = xex.pdata[i] if i < len(xex.pdata) else None
            print("no PDATA owner;",
                  f"previous {prev[0]:08X}..{prev[0] + prev[1]:08X}" if prev else "",
                  f"next {nxt[0]:08X}" if nxt else "")
    elif a.command == "xrefs":
        target = int(a.args[0], 16)
        for source in xex.branch_xrefs(target):
            print("\n".join(xex.disassemble(source, 1)))
        needle = struct.pack(">I", target)
        pos = xex.image.find(needle)
        while pos >= 0:
            if pos % 4 == 0:
                print(f"{xex.base + pos:08X}   data pointer")
            pos = xex.image.find(needle, pos + 1)
    elif a.command == "bytes":
        address = int(a.args[0], 16)
        print(xex.read(address, int(a.args[1]) if len(a.args) > 1 else 16).hex(" "))
    elif a.command == "dump":
        open(a.args[0], "wb").write(xex.image)


if __name__ == "__main__":
    sys.exit(main())
