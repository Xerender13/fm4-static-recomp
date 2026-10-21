"""Makes the shipped shader list and vertex shader table from shader caches and a disc shader dump.

Usage: python -E -P tools/shader_list.py
  fetch-table <out.tsv> <dump dir> <.xsh>...   the vertex shader table
  merge <out.xpso> <in.xpso>...                 union of pipeline lists, first input first
  check-sdk <sdk root> <shipped .xpso>          the list's version against the SDK and the tool
  verify <shareable dir> <list dir> [--before <earlier shareable dir>]
                                                a written cache against the shipped list

A dump directory holds index.tsv (HASH16, vs|ps, dword count, source; one line per distinct
microcode hash) and <HASH16>.bin with the microcode as big-endian guest bytes, as
fm4_static_recomp.exe --fm4_dump_disc_shaders writes it.

The table pairs each run-time vertex shader with the disc shader D3D made it from. D3D writes the
vertex declaration into the fetch instructions and links the shader to its pixel shader: exports
the pixel shader does not read become a no-op instruction, the rest may move to other export
registers. A line is RUNTIME16, SOURCE16, dword count, the XOR of every changed word as
index:xor8 (only fetch instruction words and export destination bits), and the indices of the
instructions replaced by the no-op. Neither field holds microcode.

Needs the xxhash package, installed in the user site, so -I (which drops it) is not used.
"""

import os
import re
import struct
import sys

import xxhash

XSH_MAGIC = 0x48534558
XSH_VERSION = 0x20201219
XPSO_MAGIC = 0x53504558
XPSO_HEADER_BYTES = 12
XPSO_RECORD_BYTES = 68
XSH_NAME = '4D530910.xsh'
XPSO_NAME = '4D530910.fbo.vk.xpso'
TABLE_HEADER = '# fm4 vertex fetch table 1'
EXEC_OPCODES = (1, 2, 3, 4, 5, 6, 13, 14)
NOP = (0xC8000000, 0x00000000, 0x02000000)
EXPORT_DEST_BITS = 0x00003F3F


def read_xsh(path):
    with open(path, 'rb') as file:
        data = file.read()
    if len(data) < 8 or struct.unpack_from('<I', data)[0] != XSH_MAGIC or \
            struct.unpack_from('>I', data, 4)[0] != XSH_VERSION:
        sys.exit(f'{path}: not a shader storage file')
    records, offset = [], 8
    while offset + 12 <= len(data):
        ucode_hash, packed = struct.unpack_from('<QI', data, offset)
        size = (packed & 0x7FFFFFFF) * 4
        ucode = data[offset + 12:offset + 12 + size]
        if len(ucode) != size or xxhash.xxh3_64_intdigest(ucode) != ucode_hash:
            break
        records.append((ucode_hash, packed >> 31, ucode))
        offset += 12 + size
    return records


def words_of(ucode):
    return struct.unpack(f'>{len(ucode) // 4}I', ucode)


def clause_instructions(words):
    """(instruction index, is fetch) for every instruction of the exec clauses."""
    n = len(words)
    instructions, end, group = {}, n // 3, 0
    while group < end and group * 3 + 2 < n:
        d0, d1, d2 = words[group * 3:group * 3 + 3]
        for lo, hi in ((d0, d1 & 0xFFFF), ((d1 >> 16) | (d2 << 16) & 0xFFFFFFFF, d2 >> 16)):
            if hi >> 12 & 0xF not in EXEC_OPCODES:
                continue
            address, count, sequence = lo & 0xFFF, lo >> 12 & 7, lo >> 16 & 0xFFF
            end = min(end, address)
            for i in range(count):
                if 3 * (address + i) + 2 < n:
                    instructions[address + i] = bool(sequence >> (2 * i) & 1)
        group += 1
    return instructions


def fetch_slots(ucode):
    """Word indices of the fetch instructions."""
    return {3 * k + j for k, fetch in clause_instructions(words_of(ucode)).items() if fetch
            for j in range(3)}


def export_instructions(words):
    return {k for k, fetch in clause_instructions(words).items() if not fetch and words[3 * k] >> 15 & 1}


def read_dump(directory):
    shaders = {}
    with open(os.path.join(directory, 'index.tsv')) as index:
        for line in index:
            ucode_hash, kind, _, _ = line.rstrip('\n').split('\t')
            with open(os.path.join(directory, ucode_hash + '.bin'), 'rb') as file:
                shaders[int(ucode_hash, 16)] = (kind, file.read())
    return shaders


def rebuild(ucode, xors, nops):
    words = list(words_of(ucode))
    for index, value in xors:
        words[index] ^= value
    for k in nops:
        words[3 * k:3 * k + 3] = NOP
    return struct.pack(f'>{len(words)}I', *words)


def derive(source, target, fetch, exports):
    """The records turning source words into target words, or the count of words they cannot express."""
    xors, nops, outside = [], [], 0
    for k in range(len(source) // 3):
        a, b = source[3 * k:3 * k + 3], target[3 * k:3 * k + 3]
        if a == b:
            continue
        if 3 * k in fetch:
            xors += [(3 * k + j, a[j] ^ b[j]) for j in range(3) if a[j] != b[j]]
        elif k in exports and b == NOP:
            nops.append(k)
        elif k in exports and a[1:] == b[1:] and (a[0] ^ b[0]) & ~EXPORT_DEST_BITS == 0:
            xors.append((3 * k, a[0] ^ b[0]))
        else:
            outside += sum(1 for j in range(3) if a[j] != b[j])
    tail = len(source) - len(source) % 3
    outside += sum(1 for i in range(tail, len(source)) if source[i] != target[i])
    return (xors, nops) if not outside else outside


def fetch_table(out_path, dump_dir, xsh_paths):
    dump = read_dump(dump_dir)
    by_size = {}
    for ucode_hash, (kind, ucode) in sorted(dump.items()):
        if kind == 'vs':
            words = words_of(ucode)
            by_size.setdefault(len(ucode), []).append((ucode_hash, words, fetch_slots(ucode),
                                                       export_instructions(words)))
    runtime = {}
    for path in xsh_paths:
        for ucode_hash, kind, ucode in read_xsh(path):
            if kind == 0:
                runtime.setdefault(ucode_hash, ucode)
    lines, no_candidate, unexpressed, failures = [], 0, 0, []
    for ucode_hash, ucode in sorted(runtime.items()):
        if ucode_hash in dump:
            lines.append(f'{ucode_hash:016X}\t{ucode_hash:016X}\t{len(ucode) // 4}\t\t')
            continue
        candidates = by_size.get(len(ucode), [])
        if not candidates:
            no_candidate += 1
            failures.append((ucode_hash, 'no disc shader of this size'))
            continue
        target, found, fewest = words_of(ucode), None, None
        for source_hash, source, fetch, exports in candidates:
            records = derive(source, target, fetch, exports)
            if not isinstance(records, int):
                found = (source_hash, records)
                break
            if fewest is None or records < fewest[1]:
                fewest = (source_hash, records)
        if found is None:
            unexpressed += 1
            failures.append((ucode_hash, f'closest {fewest[0]:016X}: {fewest[1]} words not expressible'))
            continue
        source_hash, (xors, nops) = found
        if xxhash.xxh3_64_intdigest(rebuild(dump[source_hash][1], xors, nops)) != ucode_hash:
            failures.append((ucode_hash, 'rebuilt hash differs'))
            continue
        xor_field = ','.join(f'{i}:{x:08X}' for i, x in xors)
        nop_field = ','.join(str(k) for k in nops)
        lines.append(f'{ucode_hash:016X}\t{source_hash:016X}\t{len(ucode) // 4}\t{xor_field}\t{nop_field}')
    with open(out_path, 'w', newline='\n') as out:
        out.write(TABLE_HEADER + '\n')
        out.writelines(line + '\n' for line in lines)
    print(f'rebuilt {len(lines)} of {len(runtime)} vertex shaders; no candidate {no_candidate}; '
          f'not expressible {unexpressed}')
    for ucode_hash, reason in failures[:10]:
        print(f'  {ucode_hash:016X}: {reason}')


def read_xpso(path):
    """The 12-byte header and the 68-byte records (description hash first) of a pipeline list."""
    with open(path, 'rb') as file:
        data = file.read()
    if len(data) < XPSO_HEADER_BYTES or struct.unpack_from('<I', data)[0] != XPSO_MAGIC:
        sys.exit(f'{path}: not a pipeline storage file')
    end = XPSO_HEADER_BYTES + (len(data) - XPSO_HEADER_BYTES) // XPSO_RECORD_BYTES * XPSO_RECORD_BYTES
    return data[:XPSO_HEADER_BYTES], [data[o:o + XPSO_RECORD_BYTES]
                                      for o in range(XPSO_HEADER_BYTES, end, XPSO_RECORD_BYTES)]


def record_hash(record):
    return struct.unpack_from('<Q', record)[0]


def merge(out_path, in_paths):
    header, records, seen = None, [], set()
    for path in in_paths:
        file_header, file_records = read_xpso(path)
        if header is not None and file_header != header:
            sys.exit(f'{path}: header {file_header.hex()} differs from {header.hex()}')
        header = file_header
        new = [r for r in file_records if record_hash(r) not in seen]
        seen.update(record_hash(r) for r in new)
        records += new
        print(f'{path}: +{len(new)} of {len(file_records)}')
    with open(out_path, 'wb') as out:
        out.write(header + b''.join(records))
    print(f'{len(records)} pipelines')


def constant(path, anchor, name):
    with open(path) as file:
        text = file.read()
    start = text.find(anchor)
    match = re.compile(rf'{name}\s*=\s*(0x[0-9A-Fa-f]+|\d+)').search(text, max(start, 0))
    if start < 0 or not match:
        sys.exit(f'{path}: {anchor} ... {name} not found')
    return int(match.group(1), 0)


def check_sdk(sdk_root, xpso_path):
    description = constant(os.path.join(sdk_root, 'include/rex/graphics/vulkan/pipeline_cache.h'),
                           'REXPACKEDSTRUCT(PipelineDescription', 'kVersion')
    modification = constant(os.path.join(sdk_root, 'include/rex/graphics/pipeline/shader/spirv_translator.h'),
                            'union Modification', 'kVersion')
    tool = constant(os.path.join(os.path.dirname(__file__), '../src/shader_cache/cache_writer.h'),
                    'kPipelineStorageVersion', 'kPipelineStorageVersion')
    header = struct.unpack_from('>I', read_xpso(xpso_path)[0], 8)[0]
    sdk = max(description, modification)
    print(f'SDK {sdk:08X} (description {description:08X}, modification {modification}), '
          f'tool {tool:08X}, list {header:08X}')
    if not sdk == tool == header:
        sys.exit('mismatch: regenerate the shipped list and update kPipelineStorageVersion')
    print('ok')


def xsh_hashes(path):
    """Hashes of an .xsh's valid records, and the problems found in it."""
    if not os.path.isfile(path):
        return [], [f'{path}: missing']
    with open(path, 'rb') as file:
        data = file.read()
    if len(data) < 8 or struct.unpack_from('<I', data)[0] != XSH_MAGIC or \
            struct.unpack_from('>I', data, 4)[0] != XSH_VERSION:
        return [], [f'{path}: bad header']
    hashes, problems, offset = [], [], 8
    while offset < len(data):
        if offset + 12 > len(data):
            problems.append(f'{path}: truncated record at {offset}')
            break
        ucode_hash, packed = struct.unpack_from('<QI', data, offset)
        size = (packed & 0x7FFFFFFF) * 4
        ucode = data[offset + 12:offset + 12 + size]
        if len(ucode) != size or xxhash.xxh3_64_intdigest(ucode) != ucode_hash:
            problems.append(f'{path}: record {ucode_hash:016X} at {offset} fails its hash')
            break
        hashes.append(ucode_hash)
        offset += 12 + size
    return hashes, problems


def xpso_hashes(path):
    if not os.path.isfile(path):
        return [], [f'{path}: missing']
    with open(path, 'rb') as file:
        data = file.read()
    if len(data) < XPSO_HEADER_BYTES or struct.unpack_from('<I', data)[0] != XPSO_MAGIC:
        return [], [f'{path}: bad header']
    problems = [] if (len(data) - XPSO_HEADER_BYTES) % XPSO_RECORD_BYTES == 0 else [f'{path}: partial record']
    return [record_hash(r) for r in read_xpso(path)[1]], problems


def duplicates(path, hashes):
    return [f'{path}: {len(hashes) - len(set(hashes))} duplicate records'] if len(hashes) != len(set(hashes)) else []


def verify(shareable, list_dir, before):
    xsh_path = os.path.join(shareable, XSH_NAME)
    xpso_path = os.path.join(shareable, XPSO_NAME)
    shaders, problems = xsh_hashes(xsh_path)
    pipelines, xpso_problems = xpso_hashes(xpso_path)
    problems += xpso_problems + duplicates(xsh_path, shaders) + duplicates(xpso_path, pipelines)
    shipped = [record_hash(r) for r in read_xpso(os.path.join(list_dir, XPSO_NAME))[1]]
    missing = set(shipped) - set(pipelines)
    if missing:
        problems.append(f'{xpso_path}: {len(missing)} shipped pipelines missing')
    if before:
        old_shaders, _ = xsh_hashes(os.path.join(before, XSH_NAME))
        old_pipelines, _ = xpso_hashes(os.path.join(before, XPSO_NAME))
        lost_shaders = set(old_shaders) - set(shaders)
        lost_pipelines = set(old_pipelines) - set(pipelines)
        if lost_shaders:
            problems.append(f'{xsh_path}: {len(lost_shaders)} earlier shaders lost')
        if lost_pipelines:
            problems.append(f'{xpso_path}: {len(lost_pipelines)} earlier pipelines lost')
    print(f'{len(shaders)} shaders, {len(pipelines)} pipelines')
    if problems:
        print('\n'.join(problems[:10]))
        sys.exit(1)
    print('ok')


def main():
    args = sys.argv[1:]
    if len(args) >= 4 and args[0] == 'fetch-table':
        fetch_table(args[1], args[2], args[3:])
    elif len(args) >= 3 and args[0] == 'merge':
        merge(args[1], args[2:])
    elif len(args) == 3 and args[0] == 'check-sdk':
        check_sdk(args[1], args[2])
    elif len(args) in (3, 5) and args[0] == 'verify' and (len(args) == 3 or args[3] == '--before'):
        verify(args[1], args[2], args[4] if len(args) == 5 else None)
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
