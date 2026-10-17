"""How much of a reference shader cache another one covers, for tuning the shader warm-up.

Usage: python -I tools/cache_coverage.py <reference .fbo.vk.xpso> <result .fbo.vk.xpso>

Reads the pipeline descriptions of the SDK's Vulkan pipeline storage (vulkan/pipeline_cache.h,
PipelineStoredDescription: a 12-byte file header, then 68-byte records of the description hash
followed by the description, which starts with the vertex and pixel shader hashes).
"""

import struct
import sys

HEADER_BYTES = 12
RECORD_BYTES = 68


def read(path):
    with open(path, 'rb') as file:
        data = file.read()
    pipelines, vertex, pixel = set(), set(), set()
    for offset in range(HEADER_BYTES, len(data) - RECORD_BYTES + 1, RECORD_BYTES):
        description, vertex_hash, _, pixel_hash = struct.unpack_from('<4Q', data, offset)
        pipelines.add(description)
        vertex.add(vertex_hash)
        if pixel_hash:
            pixel.add(pixel_hash)
    return {'pipelines': pipelines, 'vertex shaders': vertex, 'pixel shaders': pixel}


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    reference, result = read(sys.argv[1]), read(sys.argv[2])
    for name, wanted in reference.items():
        covered = len(wanted & result[name])
        percent = 100.0 * covered / len(wanted) if wanted else 100.0
        print(f'{name} {covered} of {len(wanted)} ({percent:.1f} %)')


if __name__ == '__main__':
    main()
