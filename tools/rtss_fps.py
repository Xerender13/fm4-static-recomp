"""rtss_fps.py: RivaTuner Statistics Server's view of running 3D applications, read from its
shared memory: the presents per second it counts for each process."""
import mmap
import struct
import time


def snapshot():
    m = mmap.mmap(-1, 4 * 1024 * 1024, "RTSSSharedMemoryV2", access=mmap.ACCESS_READ)
    sig, version, app_size, app_off, app_count = struct.unpack_from("<IIIII", m, 0)
    apps = {}
    for i in range(app_count):
        o = app_off + i * app_size
        if o + app_size > len(m):
            break
        pid, = struct.unpack_from("<I", m, o)
        if not pid:
            continue
        name = m[o + 4:o + 264].split(b"\0")[0].decode("mbcs", "replace")
        flags, t0, t1, frames, frame_time = struct.unpack_from("<IIIII", m, o + 264)
        apps[pid] = (name, flags, t0, t1, frames, frame_time)
    m.close()
    return version, apps


version, first = snapshot()
time.sleep(2)
_, second = snapshot()
print(f"shared memory version {version >> 16}.{version & 0xFFFF}")
for pid, (name, flags, t0, t1, frames, frame_time) in second.items():
    fps = 1000.0 * frames / (t1 - t0) if t1 > t0 else 0
    moved = pid in first and first[pid][3] != t1
    print(f"pid {pid} flags={flags:08X} fps={fps:.1f} frametime_us={frame_time} "
          f"updating={moved} {name[-60:]}")
