"""xex_refs.py <xex> <addr-lo-hex> <addr-hi-hex>: code that forms an address in [lo, hi) with lis +
a 16-bit displacement (addi, load or store), found by tracking lis per register inside a function.
Finds the data and string references that "xex_tool.py xrefs" misses."""
import struct
import sys

from xex_tool import Xex

xex = Xex(sys.argv[1])
lo, hi = int(sys.argv[2], 16), int(sys.argv[3], 16)
LOADS = {32: "lwz", 34: "lbz", 40: "lhz", 42: "lha", 48: "lfs", 50: "lfd"}
STORES = {36: "stw", 38: "stb", 44: "sth", 52: "stfs", 54: "stfd"}
for name, start, size, flags in xex.sections:
    if not flags & 0x20000000:
        continue
    off = start - xex.base
    high = {}
    for i in range(0, size & ~3, 4):
        word, = struct.unpack(">I", xex.image[off + i:off + i + 4])
        op, rd, ra = word >> 26, (word >> 21) & 31, (word >> 16) & 31
        imm = word & 0xFFFF
        simm = imm - 0x10000 if imm & 0x8000 else imm
        if word == 0x4E800020:
            high.clear()
        elif op == 15 and ra == 0:
            high[rd] = (imm << 16) & 0xFFFFFFFF
        elif op in (14, *LOADS, *STORES) and ra in high:
            target = (high[ra] + simm) & 0xFFFFFFFF
            if lo <= target < hi:
                kind = "addi" if op == 14 else LOADS.get(op) or STORES.get(op)
                function = xex.function_at(start + i)
                print(f"{start + i:08X} {kind:5} {target:08X}"
                      + (f" in {function[0]:08X}" if function else ""))
            if op == 14 and rd != ra:
                high.pop(rd, None)
        elif op in (14, 15, 24, 31, *LOADS) and rd in high and op not in STORES:
            high.pop(rd, None)
